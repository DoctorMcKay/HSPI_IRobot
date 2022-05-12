using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Timers;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Logging;
using HSPI_IRobot.Enums;
using IRobotLANClient;
using IRobotLANClient.Enums;

namespace HSPI_IRobot {
	public class HsRobot {
		public HsRobotState State { get; private set; } = HsRobotState.Disconnected;
		public HsRobotCannotConnectReason CannotConnectReason { get; private set; } = HsRobotCannotConnectReason.Ok;
		public string StateString { get; private set; } = "Connecting";
		public string ConnectedIp { get; private set; }
		public Robot Robot { get; private set; } = null;
		public readonly HsDevice HsDevice;
		public readonly RobotType Type;
		public readonly string Blid;
		public readonly string Password;
		
		private readonly HSPI _plugin;
		private bool _robotTypeFailedValidation = false;
		private readonly Dictionary<FeatureType, HsFeature> _features = new Dictionary<FeatureType, HsFeature>();
		private Timer _reconnectTimer = null;
		private Timer _stateUpdateDebounceTimer = null;
		
		// These properties are used for correcting the robot's erroneous phase change from "charge" to "run" when ending a job
		private DateTime _lastDockToChargeTransition = DateTime.Now;
		private MissionPhase _lastObservedMissionPhase = MissionPhase.Unknown;

		public event EventHandler OnConnectStateUpdated;
		public event EventHandler OnRobotStatusUpdated;

		public HsRobot(HSPI plugin, HsDevice hsDevice) {
			_plugin = plugin;
			HsDevice = hsDevice;
			Type = HsDevice.PlugExtraData["robottype"] == "vacuum" ? RobotType.Vacuum : RobotType.Mop;
			Blid = HsDevice.PlugExtraData["blid"];
			Password = HsDevice.PlugExtraData["password"];
		}

		public async Task AttemptConnect(string ip = null) {
			if (State == HsRobotState.Connecting) {
				_plugin.WriteLog(ELogType.Debug, "Aborting AttemptConnect because our state is already Connecting");
				return;
			}
			
			UpdateState(HsRobotState.Connecting, HsRobotCannotConnectReason.Ok, "Connecting");

			string lastKnownIp = HsDevice.PlugExtraData["lastknownip"];
			string connectIp = ip ?? lastKnownIp;
			
			_plugin.WriteLog(ELogType.Info, $"Attempting to connect to robot at IP {connectIp} ({Blid})");

			Robot = null;
			Robot robot;
			if (Type == RobotType.Vacuum) {
				robot = new RobotVacuum(connectIp, Blid, Password);
			} else {
				robot = new RobotMop(connectIp, Blid, Password);
			}
			
			robot.OnStateUpdated += HandleDataUpdate;
			robot.OnDebugOutput += (sender, args) => {
				_plugin.WriteLog(ELogType.Trace, $"[{robot.Name ?? Blid}] {args.Output}");
			};

			try {
				await robot.Connect();
			} catch (Exception ex) {
				_plugin.WriteLog(ELogType.Warning, $"Unable to connect to robot {Blid} at {connectIp}: {ex.Message}");
				string discoveredRobotIp = await FindRobot();
				if (discoveredRobotIp == null || discoveredRobotIp == connectIp) {
					// Either we couldn't discover the robot, or it has the same IP as we already tried
					UpdateState(
						HsRobotState.CannotConnect,
						discoveredRobotIp == connectIp ? HsRobotCannotConnectReason.DiscoveredCannotConnect : HsRobotCannotConnectReason.CannotDiscover,
						discoveredRobotIp == connectIp ? "Robot credentials are incorrect or another app is already connected" : "Robot was not found on the network"
					);
					EnqueueReconnectAttempt();
					return;
				}
				
				// We found it at a new IP, so let's try connecting again
				await AttemptConnect(discoveredRobotIp);
				return;
			}

			// We are now connected, but waiting to make sure the robot type is correct
			DateTime dataUpdateWaitStart = DateTime.Now;
			while (DateTime.Now.Subtract(dataUpdateWaitStart).TotalSeconds < 5) {
				if (_robotTypeFailedValidation) {
					UpdateState(HsRobotState.CannotConnect, HsRobotCannotConnectReason.CannotValidateType, "Robot credentials are correct, but are not for the expected type of robot");
					EnqueueReconnectAttempt();
					return;
				}
				
				if (Robot != null) {
					// We passed validation
					UpdateState(HsRobotState.Connected, HsRobotCannotConnectReason.Ok, "OK");
					ConnectedIp = connectIp;
					Robot.OnDisconnected += HandleDisconnect;
					Robot.OnUnexpectedValue += HandleUnexpectedValue;
					return;
				}
				
				// Still waiting on validation
				await Task.Delay(100);
			}

			UpdateState(HsRobotState.CannotConnect, HsRobotCannotConnectReason.CannotValidateType, "Timed out waiting on validation of robot type");
			await robot.Disconnect();
			EnqueueReconnectAttempt();
		}

		private async Task<string> FindRobot() {
			RobotDiscovery discovery = new RobotDiscovery();
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
			Robot.OnDisconnected -= HandleDisconnect;
			await Robot.Disconnect();
		}

		private void EnqueueReconnectAttempt() {
			_reconnectTimer?.Stop();
			_reconnectTimer = new Timer {
				AutoReset = false,
				Enabled = true,
				Interval = 30000 // 30 seconds
			};

			_reconnectTimer.Elapsed += async (src, arg) => {
				_reconnectTimer.Dispose();
				_reconnectTimer = null;

				await AttemptConnect();
			};
		}

		private void UpdateState(HsRobotState state, HsRobotCannotConnectReason cannotConnectReason, string stateString) {
			State = state;
			CannotConnectReason = cannotConnectReason;
			StateString = stateString;
			OnConnectStateUpdated?.Invoke(this, null);
		}

		private void HandleDataUpdate(object src, EventArgs arg) {
			if (Robot == null) {
				Robot srcRobot = (Robot) src;
				// We're waiting to make sure this robot is of a valid type
				if (srcRobot.IsCorrectRobotType()) {
					// All seems good!
					Robot = srcRobot;
				} else {
					_robotTypeFailedValidation = true;
					return;
				}
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
				Robot.Phase == MissionPhase.Charge
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
			
			if (Robot.Phase == MissionPhase.Run && DateTime.Now.Subtract(_lastDockToChargeTransition).TotalSeconds <= 1) {
				// Our new phase is run, but we just saw the robot dock under 1s ago. It's unlikely that the robot
				// immediately went back to running right after docking, so this is probably the robot issue we see where
				// it goes to Clean/Run for ~5 seconds before going to None/NoJob. Increase our debounce timer to 10s.
				// If the cycle changes to None within ~5s as we expect, it'll overwrite the debounce timer to 500ms
				debounceTimerInterval = 10000;
			}

			_lastObservedMissionPhase = Robot.Phase;

			_stateUpdateDebounceTimer?.Stop();
			_stateUpdateDebounceTimer = new Timer {
				AutoReset = false,
				Enabled = true,
				Interval = debounceTimerInterval
			};
			_stateUpdateDebounceTimer.Elapsed += (sender, args) => {
				_stateUpdateDebounceTimer = null;
				OnRobotStatusUpdated?.Invoke(this, null);
			};
		}

		private async void HandleDisconnect(object src, EventArgs arg) {
			Robot.OnDisconnected -= HandleDisconnect;

			_plugin.WriteLog(ELogType.Warning, $"Disconnected from robot {Robot.Name}");
			UpdateState(HsRobotState.Disconnected, HsRobotCannotConnectReason.Ok, "Disconnected");

			await Task.Delay(1000);
			await AttemptConnect();
		}

		private void HandleUnexpectedValue(object src, Robot.UnexpectedValueEventArgs arg) {
			_plugin.WriteLog(ELogType.Error, $"Unexpected {arg.ValueType} value: \"{arg.Value}\"");
		}

		public HsFeature GetFeature(FeatureType type) {
			if (_features.ContainsKey(type)) {
				return _features[type];
			}

			HsFeature feature = _plugin.GetHsController().GetFeatureByAddress($"{Blid}:{type}");
			_features.Add(type, feature);

			FeatureUpdater updater = new FeatureUpdater(_plugin);
			updater.ExecuteFeatureUpdates(feature);
			
			return feature;
		}

		public enum HsRobotState {
			Connecting,
			Connected,
			Disconnected,
			CannotConnect
		}

		public enum HsRobotCannotConnectReason {
			Ok,
			CannotDiscover,
			DiscoveredCannotConnect,
			CannotValidateType
		}
	}
}
