using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Logging;
using HSPI_IRobot.Enums;
using IRobotLANClient;
using IRobotLANClient.Enums;

namespace HSPI_IRobot {
	public class HsRobot {
		public HsRobotState State { get; private set; } = HsRobotState.Disconnected;
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
			
			State = HsRobotState.Connecting;
			StateString = "Connecting";
			
			string lastKnownIp = HsDevice.PlugExtraData["lastknownip"];
			string connectIp = ip ?? lastKnownIp;

			Robot robot;
			if (Type == RobotType.Vacuum) {
				robot = new RobotVacuum(connectIp, Blid, Password);
			} else {
				robot = new RobotMop(connectIp, Blid, Password);
			}
			
			robot.OnStateUpdated += HandleDataUpdate;

			try {
				await robot.Connect();
			} catch (Exception ex) {
				_plugin.WriteLog(ELogType.Warning, $"Unable to connect to robot {Blid} at {connectIp}: {ex.Message}");
				string discoveredRobotIp = await FindRobot();
				if (discoveredRobotIp == null || discoveredRobotIp == connectIp) {
					// Either we couldn't discover the robot, or it has the same IP as we already tried
					State = HsRobotState.CannotConnect;
					StateString = discoveredRobotIp == connectIp ? "Robot credentials are incorrect" : "Robot was not found on the network";
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
					State = HsRobotState.CannotConnect;
					StateString = "Robot credentials are correct, but are not for the expected type of robot";
				} else if (Robot != null) {
					// We passed validation
					ConnectedIp = connectIp;
					Robot.OnDisconnected += HandleDisconnect;
					Robot.OnUnexpectedValue += HandleUnexpectedValue;
					return;
				}
				
				// Still waiting on validation
				await Task.Delay(100);
			}

			State = HsRobotState.CannotConnect;
			StateString = "Timed out waiting on validation of robot type";
			await robot.Disconnect();
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
			State = HsRobotState.Disconnected;
			StateString = "Disconnected";

			await Task.Delay(1000);
			await AttemptConnect();
			
			// TODO auto-reconnect attempts if we aren't able to reconnect immediately
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
			return feature;
		}

		public enum HsRobotState {
			Connecting,
			Connected,
			Disconnected,
			CannotConnect
		}
	}
}
