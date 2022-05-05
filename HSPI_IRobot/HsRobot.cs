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
		public HsRobotCannotConnectReason CannotConnectReason { get; private set; } = HsRobotCannotConnectReason.None;
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
			
			UpdateState(HsRobotState.Connecting, HsRobotCannotConnectReason.None, "Connecting");

			string lastKnownIp = HsDevice.PlugExtraData["lastknownip"];
			string connectIp = ip ?? lastKnownIp;
			
			_plugin.WriteLog(ELogType.Debug, $"Attempting to connect to robot at IP {connectIp} ({Blid})");

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
					UpdateState(HsRobotState.Connected, HsRobotCannotConnectReason.None, "OK");
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
					State = HsRobotState.Connected;
					StateString = "OK";
				} else {
					_robotTypeFailedValidation = true;
				}
			}

			OnRobotStatusUpdated?.Invoke(this, null);
		}

		private async void HandleDisconnect(object src, EventArgs arg) {
			Robot.OnDisconnected -= HandleDisconnect;

			_plugin.WriteLog(ELogType.Warning, $"Disconnected from robot {Robot.Name}");
			UpdateState(HsRobotState.Disconnected, HsRobotCannotConnectReason.None, "Disconnected");

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
			None,
			CannotDiscover,
			DiscoveredCannotConnect,
			CannotValidateType
		}
	}
}
