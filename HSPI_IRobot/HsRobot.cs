using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Logging;
using HSPI_IRobot.Enums;
using HSPI_IRobot.FeaturePageHandlers;
using IRobotLANClient;
using IRobotLANClient.Enums;
using IRobotLANClient.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HSPI_IRobot;

public class HsRobot {
	public HsRobotState State { get; private set; } = HsRobotState.Disconnected;
	public HsRobotCannotConnectReason CannotConnectReason { get; private set; } = HsRobotCannotConnectReason.Ok;
	public string StateString { get; private set; } = "Connecting";
	public string ConnectedIp { get; private set; }
	public RobotClient Client { get; private set; } = null;

	public bool ObservedSoftwareUpdateDownload = false;

	public HsDevice HsDevice => _plugin.GetHsController().GetDeviceByRef(HsDeviceRef);
		
	public PlugExtraData PlugExtraData {
		get => (PlugExtraData) _plugin.GetHsController().GetPropertyByRef(HsDeviceRef, EProperty.PlugExtraData);
		set => _plugin.GetHsController().UpdatePropertyByRef(HsDeviceRef, EProperty.PlugExtraData, value);
	}

	public readonly int HsDeviceRef;
	public readonly RobotType Type;
	public readonly string Blid;
	[JsonIgnore] public readonly string Password;
		
	private readonly HSPI _plugin;
	[JsonProperty] private readonly Dictionary<FeatureType, HsFeature> _features = new Dictionary<FeatureType, HsFeature>();
	private OneShotTimer _reconnectTimer = null;
	private OneShotTimer _stateUpdateDebounceTimer = null;
	private DateTime _installingSoftwareUpdate = DateTime.MinValue;
		
	// These properties are used for correcting the robot's erroneous phase change from "charge" to "run" when ending a job
	private DateTime _lastDockToChargeTransition = DateTime.Now;
	private MissionPhase _lastObservedMissionPhase = MissionPhase.Unknown;
		
	// These properties are just for debug reports
	[JsonProperty] private Exception _lastConnectException;

	public event EventHandler OnConnectStateUpdated;
	public event EventHandler OnRobotStatusUpdated;

	public HsRobot(int deviceRef) {
		_plugin = HSPI.Instance;
		HsDeviceRef = deviceRef;

		try {
			PlugExtraData ped = PlugExtraData;
			Type = ped["robottype"] == "vacuum" ? RobotType.Vacuum : RobotType.Mop;
			Blid = ped["blid"];
			Password = ped["password"];
				
			_plugin.BackupPlugExtraData(deviceRef);
		} catch (KeyNotFoundException) {
			string deviceName = HsDevice.Name;
				
			WriteLog(ELogType.Error, $"HS4 device {deviceName} is corrupt. Attempting automatic repair.");
			if (!_plugin.RestorePlugExtraData(deviceRef)) {
				State = HsRobotState.FatalError;
				StateString = "HS4 device is irreparably corrupt. Delete and re-add the robot.";
				WriteLog(ELogType.Error, $"HS4 device {deviceName} is irreparably corrupt. Delete and re-add the robot.");
				return;
			}
				
			// Try to init again
			try {
				PlugExtraData ped = PlugExtraData;
				Type = ped["robottype"] == "vacuum" ? RobotType.Vacuum : RobotType.Mop;
				Blid = ped["blid"];
				Password = ped["password"];
			} catch (KeyNotFoundException) {
				State = HsRobotState.FatalError;
				StateString = "HS4 device is irreparably corrupt. Delete and re-add the robot.";
				WriteLog(ELogType.Error, $"HS4 device {deviceName} is irreparably corrupt. Delete and re-add the robot.");
			}
		}
	}

	public async Task AttemptConnect(string ip = null, bool skipStateCheck = false) {
		ObservedSoftwareUpdateDownload = false;
			
		if (State == HsRobotState.Connecting && !skipStateCheck) {
			WriteLog(ELogType.Warning, "Aborting AttemptConnect because our state is already Connecting");
			return;
		}

		if (CannotConnectReason == HsRobotCannotConnectReason.ConnectionDisabled) {
			WriteLog(ELogType.Warning, "Aborting AttemptConnect because our connection is disabled");
			return;
		}
			
		UpdateState(HsRobotState.Connecting, HsRobotCannotConnectReason.Ok, "Connecting");

		PlugExtraData ped = PlugExtraData;
		string lastKnownIp = ped.ContainsNamed("lastknownip") ? ped["lastknownip"] : null;
		string connectIp = ip ?? lastKnownIp ?? "127.0.0.1";
			
		WriteLog(ELogType.Info, $"Attempting to connect to robot at IP {connectIp} ({Blid})");

		Client = null;
		RobotClient robot;
		if (Type == RobotType.Vacuum) {
			robot = new RobotVacuumClient(connectIp, Blid, Password);
		} else {
			robot = new RobotMopClient(connectIp, Blid, Password);
		}
			
		robot.OnStateUpdated += HandleDataUpdate;
		robot.OnDebugOutput += (sender, args) => {
			WriteLog(ELogType.Trace, args.Output);
		};

		try {
			await robot.Connect();
		} catch (Exception ex) {
			_lastConnectException = ex;
				
			WriteLog(ELogType.Warning, $"Unable to connect to robot {Blid} at {connectIp}: {ex.Message}");
			string discoveredRobotIp = await FindRobot();
			if (discoveredRobotIp == null || discoveredRobotIp == connectIp) {
				// Either we couldn't discover the robot, or it has the same IP as we already tried

				HsRobotCannotConnectReason cannotConnectReason;
				string stringState;

				if (discoveredRobotIp == connectIp) {
					// We discovered it, but can't connect
					cannotConnectReason = HsRobotCannotConnectReason.DiscoveredCannotConnect;
					if (ex is RobotConnectionException exception) {
						WriteLog(
							exception.ConnectionError == ConnectionError.UnspecifiedError ? ELogType.Warning : ELogType.Debug,
							$"Connection error for {Blid}: {exception.RecursiveMessage}"
						);
							
						stringState = exception.FriendlyMessage;
					} else {
						WriteLog(ELogType.Warning, $"Unspecified connection error type for {Blid}: {ex.Message}");
						stringState = ex.Message;
					}
				} else {
					// Not discovered
					WriteLog(ELogType.Debug, $"Connection error for {Blid}: not discovered on network");
					cannotConnectReason = HsRobotCannotConnectReason.CannotDiscover;
					stringState = "Not found on the network";
				}

				UpdateState(HsRobotState.CannotConnect, cannotConnectReason, stringState);
				EnqueueReconnectAttempt();
				return;
			}
				
			// We found it at a new IP, so let's try connecting again
			WriteLog(ELogType.Debug, $"Detected {Blid} at new IP: {discoveredRobotIp}");
			await AttemptConnect(discoveredRobotIp, true);
			return;
		}

		// We are now connected, but waiting to make sure the robot type is correct
		DateTime validationWaitStart = DateTime.Now;
		bool typeValidated = await robot.WaitForTypeValidation(5000);
		WriteLog(ELogType.Debug, $"Type validation done in {DateTime.Now.Subtract(validationWaitStart).TotalMilliseconds} ms with result: {typeValidated}");
			
		if (!typeValidated) {
			await robot.Disconnect();
			UpdateState(HsRobotState.CannotConnect, HsRobotCannotConnectReason.CannotValidateType, "Could not verify robot type");
			EnqueueReconnectAttempt();
			return;
		}
			
		// We passed validation
		Client = robot;
		UpdateState(HsRobotState.Connected, HsRobotCannotConnectReason.Ok, "OK");
		ConnectedIp = connectIp;
		Client.OnDisconnected += HandleDisconnect;
		Client.OnUnexpectedValue += HandleUnexpectedValue;
	}

	public bool DisableConnection(string source = null) {
		if (State == HsRobotState.CannotConnect && CannotConnectReason == HsRobotCannotConnectReason.ConnectionDisabled) {
			return false;
		}

		string statusString = "Connection disabled";
		if (source != null) {
			statusString = $"Connection disabled by {source}";
		}
			
		WriteLog(ELogType.Info, "Disabling connection to robot");
		Disconnect();
		UpdateState(HsRobotState.CannotConnect, HsRobotCannotConnectReason.ConnectionDisabled, statusString);
		return true;
	}

	public bool EnableConnection() {
		if (State != HsRobotState.CannotConnect || CannotConnectReason != HsRobotCannotConnectReason.ConnectionDisabled) {
			return false;
		}

		WriteLog(ELogType.Info, "Attempting to re-establish connection to previously disabled robot");
		UpdateState(HsRobotState.Connecting, HsRobotCannotConnectReason.Ok, "Connecting");
		AttemptConnect(null, true).ContinueWith(_ => { });
		return true;
	}

	private async Task<string> FindRobot() {
		DiscoveryClient discovery = new DiscoveryClient();
		discovery.Discover();

		string discoveredRobotIp = null;
			
		discovery.OnRobotDiscovered += (sender, robot) => {
			if (robot.Blid == Blid) {
				discoveredRobotIp = robot.IpAddress;
			}
		};
			
		DateTime discoveryStartTime = DateTime.Now;
		while (DateTime.Now.Subtract(discoveryStartTime).TotalSeconds < 5) {
			if (discoveredRobotIp != null) {
				return discoveredRobotIp;
			}

			await Task.Delay(100);
		}

		// Discovery failed after 5 seconds
		return null;
	}

	public async void Disconnect() {
		_reconnectTimer?.Stop();

		if (Client != null) {
			Client.OnDisconnected -= HandleDisconnect;
			await Client.Disconnect();
			Client = null;
		}
	}

	private void EnqueueReconnectAttempt() {
		_reconnectTimer?.Stop();

		_reconnectTimer = new OneShotTimer(30000); // 30 seconds
		_reconnectTimer.Elapsed += async (src, arg) => {
			_reconnectTimer = null;

			await AttemptConnect();
		};
	}

	private void UpdateState(HsRobotState state, HsRobotCannotConnectReason cannotConnectReason, string stateString) {
		if (state == State && cannotConnectReason == CannotConnectReason && stateString == StateString) {
			WriteLog(ELogType.Debug, $"Attempted to update connection state but it's already identical: {state} / {cannotConnectReason} / {stateString}");
			return;
		}
			
		WriteLog(
			state == HsRobotState.Connecting || state == HsRobotState.Connected ? ELogType.Info : ELogType.Warning,
			$"{Blid} connection state update: {state} / {cannotConnectReason} / {stateString}"
		);

		if (state == HsRobotState.Connected) {
			_installingSoftwareUpdate = DateTime.MinValue;
		} else if (state == HsRobotState.CannotConnect && DateTime.Now.Subtract(_installingSoftwareUpdate).TotalMinutes <= 10) {
			// We can't connect, but it's probably because the robot is installing a software update
			stateString = "Installing software update";
		}
			
		State = state;
		CannotConnectReason = cannotConnectReason;
		StateString = stateString;
		OnConnectStateUpdated?.Invoke(this, null);
	}

	private void HandleDataUpdate(object src, EventArgs arg) {
		if (Client == null) {
			// This really shouldn't be possible, but just in case it is...
			return;
		}
			
		// When a robot's state updates, especially when docking after a job is finished, it's possible for it to
		// rapidly flip between multiple different states. It has been observed that when a robot docks, it rapidly
		// flips between Clean/UserSentHome to Clean/Stop to Clean/UserSentHome to Clean/Charge to Clean/Run to Clean/Charge
		// to Clean/Run and finally to None/Charge. All of these rapid updates can cause problems in events, so let's
		// debounce updates by waiting until the last update we received was 500ms ago.
			
		// In my experience, these rapid changes all take place within 11ms of each other, so 500ms should be more
		// than enough time to wait. The final Clean/Run to None/Charge transition can take multiple seconds though,
		// so we need a special case to handle that. Also, when starting a job it can take ~300ms for the phase to
		// go from Charge to Run.

		if (
			Client.Phase == MissionPhase.Charge
			&& (
				_lastObservedMissionPhase == MissionPhase.DockingAfterMission
				|| _lastObservedMissionPhase == MissionPhase.DockingMidMission
				|| _lastObservedMissionPhase == MissionPhase.UserSentHome
			)
		) {
			// We went from docking to charging
			_lastDockToChargeTransition = DateTime.Now;
		}

		double debounceTimerInterval = 500;
			
		if (Client.Phase == MissionPhase.Run && DateTime.Now.Subtract(_lastDockToChargeTransition).TotalSeconds <= 1) {
			// Our new phase is run, but we just saw the robot dock under 1s ago. It's unlikely that the robot
			// immediately went back to running right after docking, so this is probably the robot issue we see where
			// it goes to Clean/Run for ~5 seconds before going to None/NoJob. Increase our debounce timer to 10s.
			// If the cycle changes to None within ~5s as we expect, it'll overwrite the debounce timer to 500ms
			debounceTimerInterval = 10000;
		}

		_lastObservedMissionPhase = Client.Phase;

		_stateUpdateDebounceTimer?.Stop();

		_stateUpdateDebounceTimer = new OneShotTimer(debounceTimerInterval);
		_stateUpdateDebounceTimer.Elapsed += (sender, args) => {
			_stateUpdateDebounceTimer = null;
			OnRobotStatusUpdated?.Invoke(this, null);
		};
			
		// Let's handle OTA update progress
		// When updating to 22.14.1 on May 30, 2022 my i7 was observed flipping "deploymentState" from 0
		// to 1 to 2 to 3, then otaDownloadProgress went from 0 to 100, then notReady was set to 18, then
		// deploymentState flipped from 3 to 4, and finally lastDisconnect was set to 3 before it disconnected.
		// I'm not sure exactly which of these happens with every OTA update (deploymentState seems likely to always
		// go to 4 before disconnecting, but not 100% sure) so let's just check the download percentage.
		if (Client.SoftwareUpdateDownloadProgress > 0) {
			WriteLog(ELogType.Info, $"Downloading software update: {Client.SoftwareUpdateDownloadProgress}%");
			StateString = $"OK (Downloading software update {Client.SoftwareUpdateDownloadProgress}%)";

			_installingSoftwareUpdate = Client.SoftwareUpdateDownloadProgress >= 95 ? DateTime.Now : DateTime.MinValue;
		} else {
			_installingSoftwareUpdate = DateTime.MinValue;
		}
	}

	private async void HandleDisconnect(object src, EventArgs arg) {
		Client.OnDisconnected -= HandleDisconnect;

		WriteLog(ELogType.Warning, $"Disconnected from robot");
		UpdateState(HsRobotState.Disconnected, HsRobotCannotConnectReason.Ok, "Disconnected");

		await Task.Delay(1000);
		await AttemptConnect();
	}

	private void HandleUnexpectedValue(object src, RobotClient.UnexpectedValueEventArgs arg) {
		WriteLog(ELogType.Error, $"Unexpected {arg.ValueType} value: \"{arg.Value}\"");
	}

	public HsFeature GetFeature(FeatureType type) {
		if (_features.ContainsKey(type)) {
			return _features[type];
		}

		HsFeature feature = _plugin.GetHsController().GetFeatureByAddress($"{Blid}:{type}");
		if (feature == null) {
			WriteLog(ELogType.Warning, $"Missing feature {type} for {Blid}; creating it");
			FeatureCreator creator = new FeatureCreator(HsDevice);
			feature = _plugin.GetHsController().GetFeatureByRef(creator.CreateFeature(type));
		}
		_features.Add(type, feature);

		FeatureUpdater updater = new FeatureUpdater();
		updater.ExecuteFeatureUpdates(feature);
			
		return feature;
	}

	public double GetFeatureValue(FeatureType type) {
		return (double) _plugin.GetHsController().GetPropertyByRef(GetFeature(type).Ref, EProperty.Value);
	}

	public string GetName() {
		if (Client?.Name != null) {
			return Client.Name;
		}

		HsDevice device = _plugin.GetHsController().GetDeviceByAddress(Blid);
		return device.Name;
	}

	public bool IsNavigating() {
		// Check whether we're navigating *based on HS feature status*
		if (
			!Enum.TryParse(GetFeatureValue(FeatureType.Status).ToString(CultureInfo.InvariantCulture), out RobotStatus cycle)
			|| !Enum.TryParse(GetFeatureValue(FeatureType.JobPhase).ToString(CultureInfo.InvariantCulture), out CleanJobPhase phase)
		) {
			WriteLog(ELogType.Warning, $"Unable to parse {Blid} status ({GetFeatureValue(FeatureType.Status)}) or phase ({GetFeatureValue(FeatureType.JobPhase)})");
			return false;
		}
			
		return new[] {RobotStatus.Clean, RobotStatus.DockManually, RobotStatus.Train}.Contains(cycle)
		       && new[] {CleanJobPhase.Cleaning, CleanJobPhase.LowBatteryReturningToDock, CleanJobPhase.DoneReturningToDock}.Contains(phase);
	}

	public List<FavoriteJobs.FavoriteJob> GetFavoriteJobs() {
		PlugExtraData ped = PlugExtraData;
		return !ped.ContainsNamed("favoritejobs")
			? new List<FavoriteJobs.FavoriteJob>()
			: JArray.Parse(ped["favoritejobs"]).Select(token => token.ToObject<FavoriteJobs.FavoriteJob>()).ToList();
	}

	public void SaveFavoriteJobs(List<FavoriteJobs.FavoriteJob> favorites) {
		PlugExtraData ped = PlugExtraData;
		string serializedFavorites = JsonConvert.SerializeObject(favorites);
			
		if (!ped.ContainsNamed("favoritejobs")) {
			ped.AddNamed("favoritejobs", serializedFavorites);
		} else {
			ped["favoritejobs"] = serializedFavorites;
		}

		PlugExtraData = ped;
	}

	public bool StartFavoriteJob(string jobName) {
		if (Client == null || State != HsRobotState.Connected) {
			return false;
		}
			
		FavoriteJobs.FavoriteJob favorite = GetFavoriteJobs().Find(job => job.Name == jobName);
		if (favorite.Equals(default(FavoriteJobs.FavoriteJob))) {
			return false;
		}
			
		foreach (JProperty prop in favorite.Command.Properties().Where(prop => prop.Value.Type == JTokenType.Null).ToArray()) {
			favorite.Command.Remove(prop.Name);
		}

		// The robot will error if we send it a pmap version id that it doesn't expect (it changes over time), but
		// it appears that sending no pmapv id works fine. So let's do that. The only alternative I can think of is
		// checking against the cloud, but that's very un-ideal.
		if (favorite.Command.ContainsKey("user_pmapv_id")) {
			favorite.Command.Remove("user_pmapv_id");
		}

		Client.CleanCustom(favorite.Command);
		return true;
	}

	public ConfigOption[] GetSupportedOptions() {
		if (Client == null || State != HsRobotState.Connected) {
			return null;
		}
			
		return Enum.GetValues(typeof(ConfigOption)).OfType<ConfigOption>()
			.Where(option => Client.SupportsConfigOption(option))
			.ToArray();
	}

	private void WriteLog(ELogType logType, string message, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string caller = null) {
		// ReSharper disable once ExplicitCallerInfoArgument
		_plugin.WriteLog(logType, $"[{GetName()}] {message}", lineNumber, caller);
	}

	public enum HsRobotState {
		Connecting,
		Connected,
		Disconnected,
		CannotConnect,
		FatalError
	}

	public enum HsRobotCannotConnectReason {
		Ok,
		CannotDiscover,
		DiscoveredCannotConnect,
		CannotValidateType,
		ConnectionDisabled
	}
}