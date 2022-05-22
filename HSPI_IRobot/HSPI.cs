using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Devices.Controls;
using HomeSeer.PluginSdk.Devices.Identification;
using HomeSeer.PluginSdk.Logging;
using HSPI_IRobot.Enums;
using IRobotLANClient;
using IRobotLANClient.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HSPI_IRobot {
	public class HSPI : AbstractPlugin {
		public static HSPI Instance { get; private set; }
		
		public const string PluginId = "iRobot";
		public override string Name { get; } = "iRobot";
		public override string Id { get; } = PluginId;

		private bool _debugLogging;

		private List<HsRobot> _hsRobots;
		private RobotDiscovery _robotDiscovery;
		private RobotCloudAuth _robotCloudAuth;
		private string _addRobotResult;
		private AnalyticsClient _analyticsClient;

		public HSPI() {
			Instance = this;
		}

		protected override void Initialize() {
			string pluginVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
			string irobotClientVersion = FileVersionInfo.GetVersionInfo(Assembly.GetAssembly(typeof(Robot)).Location).FileVersion;
			
#if DEBUG
			WriteLog(ELogType.Info, $"Plugin version {pluginVersion} starting with client version {irobotClientVersion}");
#else
			if (pluginVersion != irobotClientVersion) {
				WriteLog(ELogType.Warning, $"Running plugin version is {pluginVersion} but IRobotLANClient.dll version is {irobotClientVersion}");
			} else {
				WriteLog(ELogType.Info, $"Starting iRobot plugin version {pluginVersion}");
			}
#endif
			
			_analyticsClient = new AnalyticsClient(this, HomeSeerSystem);
			
			// Build the settings page
			PageFactory settingsPageFactory = PageFactory
				.CreateSettingsPage("iRobotSettings", "iRobot Settings")
				.WithLabel("plugin_status", "Status (refresh to update)", "x")
				.WithLabel("robots_link", "Robots", "<a href=\"/iRobot/robots.html\">Manage Robots</a>")
				.WithGroup("debug_group", "<hr>", new AbstractView[] {
					new LabelView("debug_support_link", "Support and Documentation", "<a href=\"https://forums.homeseer.com/forum/hs4-products/hs4-plugins/robotics-plug-ins-aa/irobot-dr-mckay/1544987-irobot-hs4-plugin-manual\" target=\"_blank\">HomeSeer Forum</a>"), 
					new LabelView("debug_donate_link", "Fund Future Development", "This plugin is and always will be free.<br /><a href=\"https://github.com/sponsors/DoctorMcKay\" target=\"_blank\">Please consider donating to fund future development.</a>"),
					new LabelView("debug_system_id", "System ID (include this with any support requests)", _analyticsClient.CustomSystemId),
					#if DEBUG
						new LabelView("debug_log", "Enable Debug Logging", "ON - DEBUG BUILD")
					#else
						new ToggleView("debug_log", "Enable Debug Logging")
					#endif
				});

			Settings.Add(settingsPageFactory.Page);
			
			HomeSeerSystem.RegisterDeviceIncPage(Id, "robots.html", "Manage Robots");
			
			// Initialize our device list
			InitializeDeviceList();
			
			_analyticsClient.ReportIn(5000);
		}

		private async void InitializeDeviceList() {
			_hsRobots = new List<HsRobot>();

			await Task.WhenAll(HomeSeerSystem.GetRefsByInterface(Id, true).Select(deviceRef => Task.Run(async () => {
				await InitializeDevice(HomeSeerSystem.GetDeviceByRef(deviceRef));
			})).ToList());

			int countRobots = _hsRobots.Count;
			int connectedRobots = _hsRobots.FindAll(robot => robot.State == HsRobot.HsRobotState.Connected).Count;
			WriteLog(ELogType.Info, $"All devices initialized. Found {countRobots} robots and {connectedRobots} connected successfully");
		}

		private async Task InitializeDevice(HsDevice device) {
			HsRobot robot = new HsRobot(this, device);
			_hsRobots.Add(robot);

			robot.OnConnectStateUpdated += HandleRobotConnectionStateUpdate;
			robot.OnRobotStatusUpdated += HandleRobotStatusUpdate;

			await robot.AttemptConnect();
			string robotName = robot.State == HsRobot.HsRobotState.Connected ? robot.Robot.Name : robot.Blid;
			WriteLog(ELogType.Debug, $"Initial connection attempt to {robotName} finished with new state {robot.State} ({robot.StateString})");

			if (robot.State == HsRobot.HsRobotState.Connected) {
				PlugExtraData extraData = device.PlugExtraData;
				extraData["lastknownip"] = robot.ConnectedIp;
				HomeSeerSystem.UpdatePropertyByRef(device.Ref, EProperty.PlugExtraData, extraData);
			}
		}

		public override void SetIOMulti(List<ControlEvent> colSend) {
			foreach (ControlEvent ctrl in colSend) {
				HsFeature feature = HomeSeerSystem.GetFeatureByRef(ctrl.TargetRef);
				string[] addressParts = feature.Address.Split(':');
				HsRobot robot = _hsRobots.Find(r => r.Blid == addressParts[0]);
				if (robot?.Robot == null) {
					WriteLog(ELogType.Warning, $"Got SetIOMulti {ctrl.TargetRef} = {ctrl.ControlValue}, but no such robot found or not connected");
					continue;
				}

				RobotStatus command = (RobotStatus) ctrl.ControlValue;
				switch (command) {
					case RobotStatus.Clean:
						robot.Robot.Clean();
						break;
					
					case RobotStatus.OffBaseNoJob:
						robot.Robot.Stop();
						break;
					
					case RobotStatus.JobPaused:
						robot.Robot.Pause();
						break;
					
					case RobotStatus.Resume:
						robot.Robot.Resume();
						break;
					
					case RobotStatus.DockManually:
						robot.Robot.Dock();
						break;
					
					case RobotStatus.Find:
						robot.Robot.Find();
						break;
					
					case RobotStatus.Evac:
						if (robot.Type == RobotType.Vacuum) {
							RobotVacuum roboVac = (RobotVacuum) robot.Robot;
							roboVac.Evac();
						}

						break;
					
					case RobotStatus.Train:
						robot.Robot.Train();
						break;
				}
			}
		}

		private void HandleRobotConnectionStateUpdate(object src, EventArgs arg) {
			HsRobot robot = (HsRobot) src;
			WriteLog(
				robot.State == HsRobot.HsRobotState.Connecting || robot.State == HsRobot.HsRobotState.Connected ? ELogType.Info : ELogType.Warning,
				$"Robot {robot.Blid} connection state update: {robot.State} / {robot.CannotConnectReason} / {robot.StateString}"
			);

			HsFeature errorFeature = robot.GetFeature(FeatureType.Error);

			switch (robot.State) {
				case HsRobot.HsRobotState.Connected:
					HomeSeerSystem.UpdateFeatureValueByRef(errorFeature.Ref, (double) InternalError.None);
					break;
				
				case HsRobot.HsRobotState.Disconnected:
					HomeSeerSystem.UpdateFeatureValueByRef(errorFeature.Ref, (double) InternalError.DisconnectedFromRobot);
					break;
				
				case HsRobot.HsRobotState.CannotConnect:
					switch (robot.CannotConnectReason) {
						case HsRobot.HsRobotCannotConnectReason.CannotDiscover:
							HomeSeerSystem.UpdateFeatureValueByRef(errorFeature.Ref, (double) InternalError.CannotDiscoverRobot);
							break;
						
						case HsRobot.HsRobotCannotConnectReason.DiscoveredCannotConnect:
						case HsRobot.HsRobotCannotConnectReason.CannotValidateType:
							HomeSeerSystem.UpdateFeatureValueByRef(errorFeature.Ref, (double) InternalError.CannotConnectToMqtt);
							break;
						
						default:
							HomeSeerSystem.UpdateFeatureValueByRef(errorFeature.Ref, (double) InternalError.DisconnectedFromRobot);
							break;
					}

					break;
				
				// We purposefully don't have a default case because we don't want to update the error feature value when reconnecting
			}
		}

		private void HandleRobotStatusUpdate(object src, EventArgs arg) {
			HsRobot robot = (HsRobot) src;
			WriteLog(ELogType.Debug, $"Robot {robot.Robot.Name} updated: battery {robot.Robot.BatteryLevel}; cycle {robot.Robot.Cycle}; phase {robot.Robot.Phase}");

			// Features common to all robot types
			// Status
			RobotStatus status = RobotStatus.OffBaseNoJob;
			switch (robot.Robot.Cycle) {
				case MissionCycle.None:
					status = robot.Robot.Phase == MissionPhase.Stop ? RobotStatus.OffBaseNoJob : RobotStatus.OnBase;
					break;
				
				case MissionCycle.Clean:
				case MissionCycle.Spot:
					switch (robot.Robot.Phase) {
						case MissionPhase.Stop:
							status = RobotStatus.JobPaused;
							break;

						default:
							status = RobotStatus.Clean;
							break;
					}
					break;
				
				case MissionCycle.Dock:
					status = RobotStatus.DockManually;
					break;
				
				case MissionCycle.Evac:
					status = RobotStatus.Evac;
					break;
				
				case MissionCycle.Train:
					status = RobotStatus.Train;
					break;
			}

			if (robot.Robot.Phase == MissionPhase.Stuck) {
				// Regardless of what our cycle is, if we're stuck we're stuck
				status = RobotStatus.Stuck;
			}
			
			HsFeature feature = robot.GetFeature(FeatureType.Status);
			HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, (double) status);
			
			// Job phase
			CleanJobPhase jobPhase = CleanJobPhase.NoJob;
			switch (status) {
				case RobotStatus.Evac:
					jobPhase = CleanJobPhase.Evac;
					break;
				
				case RobotStatus.DockManually:
					jobPhase = CleanJobPhase.DoneReturningToDock;
					break;
				
				case RobotStatus.Clean:
					switch (robot.Robot.Phase) {
						case MissionPhase.Charge:
							jobPhase = CleanJobPhase.Charging;
							break;
						
						case MissionPhase.Evac:
							jobPhase = CleanJobPhase.Evac;
							break;
						
						case MissionPhase.DockingMidMission:
							jobPhase = CleanJobPhase.LowBatteryReturningToDock;
							break;
						
						case MissionPhase.DockingAfterMission:
						case MissionPhase.UserSentHome:
							jobPhase = CleanJobPhase.DoneReturningToDock;
							break;
						
						case MissionPhase.ChargingError:
							jobPhase = CleanJobPhase.ChargingError;
							break;
						
						default:
							jobPhase = CleanJobPhase.Cleaning;
							break;
					}
					break;
				
				default:
					jobPhase = CleanJobPhase.NoJob;
					break;
			}

			feature = robot.GetFeature(FeatureType.JobPhase);
			HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, (double) jobPhase);
			
			// Battery
			bool isCharging = robot.Robot.BatteryLevel < 100 && robot.Robot.Phase == MissionPhase.Charge;
			feature = robot.GetFeature(FeatureType.Battery);
			HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, robot.Robot.BatteryLevel);
			HomeSeerSystem.UpdateFeatureValueStringByRef(
				feature.Ref,
				isCharging ? $"{robot.Robot.BatteryLevel}% (charging)" : ""
			);
			
			// Ready
			feature = robot.GetFeature(FeatureType.Ready);
			HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, robot.Robot.NotReadyCode);
			
			// Error
			feature = robot.GetFeature(FeatureType.Error);
			HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, robot.Robot.ErrorCode);

			switch (robot.Type) {
				case RobotType.Vacuum:
					RobotVacuum roboVac = (RobotVacuum) robot.Robot;
					
					// Bin
					feature = robot.GetFeature(FeatureType.VacuumBin);
					HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, (double) roboVac.BinStatus);
					
					break;
				
				case RobotType.Mop:
					RobotMop roboMop = (RobotMop) robot.Robot;
					
					// Tank
					feature = robot.GetFeature(FeatureType.MopTank);
					HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, (double) roboMop.TankStatus);
					
					// Pad type
					feature = robot.GetFeature(FeatureType.MopPad);
					HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, (double) roboMop.MopPadType);
					
					break;
			}
		}
		
		protected override void OnSettingsLoad() {
			// Called when the settings page is loaded. Use to pre-fill the inputs.
			string statusText = Status.Status.ToString().ToUpper();
			if (Status.StatusText.Length > 0) {
				statusText += ": " + Status.StatusText;
			}
			((LabelView) Settings.Pages[0].GetViewById("plugin_status")).Value = statusText;
		}

		protected override bool OnSettingChange(string pageId, AbstractView currentView, AbstractView changedView) {
			WriteLog(ELogType.Debug, $"Request to save setting {currentView.Id} on page {pageId}");

			if (pageId != "iRobotSettings") {
				WriteLog(ELogType.Warning, $"Request to save settings on unknown page {pageId}!");
				return true;
			}

			switch (currentView.Id) {
				case "debug_log":
					_debugLogging = changedView.GetStringValue() == "True";
					return true;
			}
			
			WriteLog(ELogType.Info, $"Request to save unknown setting {currentView.Id}");
			return false;
		}

		protected override void BeforeReturnStatus() {
			
		}

		public override string PostBackProc(string page, string data, string user, int userRights) {
			WriteLog(ELogType.Trace, $"PostBackProc page name {page} by user {user} with rights {userRights}");
			
			if ((userRights & 2) != 2) {
				return JsonConvert.SerializeObject(new {
					error = "Access Denied",
					fatal = true
				});
			}
			
			switch (page) {
				case "robots.html":
					return HandleRobotsPagePostBack(data);

				default:
					WriteLog(ELogType.Warning, $"Received PostBackProc for unknown page {page}");
					break;
			}

			return "";
		}

		private string HandleRobotsPagePostBack(string data) {
			JObject payload = JObject.Parse(data);
			string cmd = (string) payload.SelectToken("cmd");
			string badCmdResponse = JsonConvert.SerializeObject(new { error = "Invalid cmd" });
			string successResponse = JsonConvert.SerializeObject(new { success = true });
			if (cmd == null) {
				return badCmdResponse;
			}
			
			// Shared variables between multiple cases
			string blid;
			HsRobot robot;

			switch (cmd) {
				case "autodiscover":
					_robotDiscovery = new RobotDiscovery();
					_robotDiscovery.Discover();
					return successResponse;
				
				case "autodiscoverResult":
					return _robotDiscovery == null
						? badCmdResponse 
						: JsonConvert.SerializeObject(new { discoveredRobots = _robotDiscovery.DiscoveredRobots });

				case "addRobot":
					string ip = (string) payload.SelectToken("ip");
					blid = (string) payload.SelectToken("blid");
					string password = (string) payload.SelectToken("password");

					if (_hsRobots.Exists(robo => robo.Blid == blid)) {
						return JsonConvert.SerializeObject(new { error = "Robot already exists" });
					}
					
					_addNewRobot(ip, blid, password);
					return successResponse;
				
				case "addRobotResult":
					switch (_addRobotResult) {
						case null:
							return badCmdResponse;
						
						case "OK":
							return successResponse;
						
						default:
							return JsonConvert.SerializeObject(new { error = _addRobotResult });
					}

				case "getRobots":
					object[] robots = new object[_hsRobots.Count];
					for (int i = 0; i < _hsRobots.Count; i++) {
						robots[i] = new {
							blid = _hsRobots[i].Blid,
							password = _hsRobots[i].Password,
							stateString = _hsRobots[i].StateString,
							ip = _hsRobots[i].ConnectedIp,
							type = _hsRobots[i].Type == RobotType.Vacuum ? "vacuum" : "mop",
							name = _hsRobots[i].GetName(),
							sku = _hsRobots[i].Robot?.Sku ?? "unknown"
						};
					}
					
					return JsonConvert.SerializeObject(new { robots });
				
				case "getRobotFullStatus":
					blid = (string) payload.SelectToken("blid");
					if (blid == null) {
						return badCmdResponse;
					}

					robot = _hsRobots.Find(bot => bot.Blid == blid);
					return robot == null
						? JsonConvert.SerializeObject(new { error = "Invalid blid" })
						: JsonConvert.SerializeObject(new { status = robot.Robot?.ReportedState });
				
				case "cloudLogin":
					string cloudUsername = (string) payload.SelectToken("username");
					string cloudPassword = (string) payload.SelectToken("password");
					if (cloudUsername == null || cloudPassword == null) {
						return badCmdResponse;
					}

					_robotCloudAuth = new RobotCloudAuth(cloudUsername, cloudPassword);
					_robotCloudAuth.Login();
					return successResponse;
				
				case "cloudLoginResult":
					if (_robotCloudAuth == null || _robotCloudAuth.LoginInProcess) {
						return badCmdResponse;
					}

					if (_robotCloudAuth.LoginError != null) {
						return JsonConvert.SerializeObject(new {
							error = _robotCloudAuth.LoginError.Message
						});
					}

					return JsonConvert.SerializeObject(new {
						robots = _robotCloudAuth.Robots
					});
				
				case "deleteRobot":
					blid = (string) payload.SelectToken("blid");

					robot = _hsRobots.Find(r => r.Blid == blid);
					if (robot == null) {
						return badCmdResponse;
					}
					
					robot.Disconnect();
					_hsRobots.Remove(robot);

					HsDevice device = HomeSeerSystem.GetDeviceByAddress(blid);
					HomeSeerSystem.DeleteDevice(device.Ref);
					return successResponse;
				
				case "debugReport":
					// We're going to do this synchronously because it shouldn't happen often
					Task<AnalyticsClient.DebugReportResponse> reportTask = _analyticsClient.DebugReport(new { Robots = _hsRobots });
					reportTask.Wait();
					AnalyticsClient.DebugReportResponse response = reportTask.Result;
					return response.Success
						? JsonConvert.SerializeObject(new { report_id = response.Message })
						: JsonConvert.SerializeObject(new { error = response.Message });

				default:
					return badCmdResponse;
			}
		}

		private async void _addNewRobot(string ip, string blid, string password) {
			_addRobotResult = null;
			
			// First things first, let's try to connect and see if we can
			try {
				RobotVerifier verifier = new RobotVerifier(ip, blid, password);
				await verifier.Connect();

				verifier.OnStateUpdated += async (src, arg) => {
					await verifier.Disconnect();
					
					if (verifier.DetectedType == RobotType.Unrecognized) {
						WriteLog(ELogType.Debug, "Unrecognized robot type");
						WriteLog(ELogType.Debug, verifier.ReportedState.ToString());
						_addRobotResult = "Unrecognized robot type";
						return;
					}
					
					// Successfully connected and recognized robot type
					_addRobotResult = "OK";
					await Task.Delay(1000);
					_createNewRobotDevice(ip, blid, password, verifier);
				};
			} catch (Exception ex) {
				// Failed to connect
				_addRobotResult = ex.Message;
			}
		}

		private async void _createNewRobotDevice(string ip, string blid, string password, RobotVerifier verifier) {
			PlugExtraData extraData = new PlugExtraData();
			extraData.AddNamed("lastknownip", ip);
			extraData.AddNamed("blid", blid);
			extraData.AddNamed("password", password);
			extraData.AddNamed("robottype", verifier.DetectedType == RobotType.Vacuum ? "vacuum" : "mop");
			extraData.AddNamed("version", "1");

			DeviceFactory factory = DeviceFactory.CreateDevice(Id)
				.WithName(verifier.Name)
				.WithAddress(blid)
				.WithExtraData(extraData);

			int newDeviceRef = HomeSeerSystem.CreateDevice(factory.PrepareForHs());
			WriteLog(ELogType.Info, $"Created new device {newDeviceRef} for {verifier.DetectedType} robot {verifier.Name} ({blid})");

			FeatureCreator featureCreator = new FeatureCreator(HomeSeerSystem.GetDeviceByRef(newDeviceRef));
			featureCreator.CreateFeature(FeatureType.Status);
			featureCreator.CreateFeature(FeatureType.JobPhase);
			featureCreator.CreateFeature(FeatureType.Battery);
			
			switch (verifier.DetectedType) {
				case RobotType.Vacuum:
					featureCreator.CreateFeature(FeatureType.VacuumBin);
					break;
				
				case RobotType.Mop:
					featureCreator.CreateFeature(FeatureType.MopTank);
					featureCreator.CreateFeature(FeatureType.MopPad);
					break;
			}

			featureCreator.CreateFeature(FeatureType.Ready);
			featureCreator.CreateFeature(FeatureType.Error);

			if (verifier.DetectedType == RobotType.Vacuum) {
				// Not all vacuums can self-empty their bins, but definitely no mops can so this is good enough
				// There isn't a good way to definitively tell if a robot supports self-empty unless it's presently
				// on a self-empty dock.
				HomeSeerSystem.AddStatusControlToFeature(
					HomeSeerSystem.GetFeatureByAddress($"{blid}:Status").Ref,
					new StatusControl(EControlType.Button) { Label = "Empty Bin", TargetValue = (double) RobotStatus.Evac }
				);
			}
			
			if (verifier.CanLearnMaps) {
				HomeSeerSystem.AddStatusControlToFeature(
					HomeSeerSystem.GetFeatureByAddress($"{blid}:Status").Ref,
					new StatusControl(EControlType.Button) { Label = "Mapping Run", TargetValue = (double) RobotStatus.Train }
				);
			}

			await InitializeDevice(HomeSeerSystem.GetDeviceByRef(newDeviceRef));
		}

		public IHsController GetHsController() {
			return HomeSeerSystem;
		}

		public HsVersion GetHsVersion() {
			string[] versionParts = HomeSeerSystem.Version().Split('.');
			return new HsVersion {
				Major = int.Parse(versionParts[0]),
				Minor = int.Parse(versionParts[1]),
				Patch = int.Parse(versionParts[2]),
				Build = int.Parse(versionParts[3])
			};
		}

		public void WriteLog(ELogType logType, string message, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string caller = null) {
#if DEBUG
			bool isDebugMode = true;

			// Prepend calling function and line number
			message = $"[{caller}:{lineNumber}] {message}";
			
			// Also print to console in debug builds
			string type = logType.ToString().ToLower();
			Console.WriteLine($"[{type}] {message}");
#else
			if (logType == ELogType.Trace) {
				// Don't record Trace events in production builds even if debug logging is enabled
				return;
			}

			bool isDebugMode = _debugLogging;
#endif

			if (logType <= ELogType.Debug && !isDebugMode) {
				return;
			}
			
			HomeSeerSystem.WriteLog(logType, message, Name);
		}

		public struct HsVersion {
			public int Major;
			public int Minor;
			public int Patch;
			public int Build;
		}
	}
}