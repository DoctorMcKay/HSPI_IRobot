using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Devices.Controls;
using HomeSeer.PluginSdk.Logging;
using HSPI_IRobot.Enums;
using IRobotLANClient;
using IRobotLANClient.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HSPI_IRobot {
	public class HSPI : AbstractPlugin {
		public override string Name { get; } = "iRobot";
		public override string Id { get; } = "iRobot";

		private bool _debugLogging;

		private List<HsRobot> _hsRobots;
		private RobotDiscovery _robotDiscovery;
		private string _addRobotResult;

		protected override void Initialize() {
			WriteLog(ELogType.Debug, "Initializing");
			
			AnalyticsClient analytics = new AnalyticsClient(this, HomeSeerSystem);
			
			// Build the settings page
			PageFactory settingsPageFactory = PageFactory
				.CreateSettingsPage("iRobotSettings", "iRobot Settings")
				.WithLabel("plugin_status", "Status (refresh to update)", "x")
				.WithGroup("debug_group", "<hr>", new AbstractView[] {
					new LabelView("debug_support_link", "Documentation", "<a href=\"https://github.com/DoctorMcKay/HSPI_iRobot/blob/master/README.md\" target=\"_blank\">GitHub</a>"), 
					new LabelView("debug_donate_link", "Fund Future Development", "This plugin is and always will be free.<br /><a href=\"https://github.com/sponsors/DoctorMcKay\" target=\"_blank\">Please consider donating to fund future development.</a>"),
					new LabelView("debug_system_id", "System ID (include this with any support requests)", analytics.CustomSystemId),
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
			
			analytics.ReportIn(5000);
		}

		private async void InitializeDeviceList() {
			_hsRobots = new List<HsRobot>();

			await Task.WhenAll(HomeSeerSystem.GetRefsByInterface(Id, true).Select(deviceRef => Task.Run(async () => {
				HsDevice device = HomeSeerSystem.GetDeviceByRef(deviceRef);
				HsRobot robot = new HsRobot(this, device);
				_hsRobots.Add(robot);

				robot.OnRobotStatusUpdated += HandleRobotStatusUpdate;

				await robot.AttemptConnect();
				string robotName = robot.State == HsRobot.HsRobotState.Connected ? robot.Robot.Name : robot.Blid;
				WriteLog(ELogType.Info, $"Connection attempt to {robotName} finished with new state {robot.State} ({robot.StateString})");

				if (robot.State == HsRobot.HsRobotState.Connected) {
					PlugExtraData extraData = device.PlugExtraData;
					extraData["lastknownip"] = robot.ConnectedIp;
					HomeSeerSystem.UpdatePropertyByRef(device.Ref, EProperty.PlugExtraData, extraData);
				}
			})).ToList());

			int countRobots = _hsRobots.Count;
			int connectedRobots = _hsRobots.FindAll(robot => robot.State == HsRobot.HsRobotState.Connected).Count;
			WriteLog(ELogType.Info, $"All devices initialized. Found {countRobots} robots and {connectedRobots} connected successfully");
		}

		public override void SetIOMulti(List<ControlEvent> colSend) {
			foreach (ControlEvent ctrl in colSend) {
				HsFeature feature = HomeSeerSystem.GetFeatureByRef(ctrl.TargetRef);
				string[] addressParts = feature.Address.Split(':');
				HsRobot robot = _hsRobots.Find(robo => robo.Blid == addressParts[0]);
				if (robot == null) {
					WriteLog(ELogType.Warning, $"Got SetIOMulti {ctrl.TargetRef} = {ctrl.ControlValue}, but no such robot found");
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
				}
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
						
						case MissionPhase.Stuck:
							status = RobotStatus.Stuck;
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
			feature = robot.GetFeature(FeatureType.Battery);
			HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, robot.Robot.BatteryLevel);
			HomeSeerSystem.UpdateFeatureValueStringByRef(
				feature.Ref,
				$"{robot.Robot.BatteryLevel}%" + (robot.Robot.BatteryLevel < 100 && robot.Robot.Phase == MissionPhase.Charge ? " (charging)" : "")
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
			WriteLog(ELogType.Debug, $"PostBackProc page name {page} by user {user} with rights {userRights}");
			
			if ((userRights & 2) != 2) {
				return JsonConvert.SerializeObject(new { error = "You do not have administrative privileges." });
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
							return JsonConvert.SerializeObject(new { success = false, error = _addRobotResult });
					}

				case "getRobots":
					JObject[] robots = new JObject[_hsRobots.Count];
					for (int i = 0; i < _hsRobots.Count; i++) {
						JObject robotData = new JObject();
						robotData.Add("blid", JToken.FromObject(_hsRobots[i].Blid));
						robotData.Add("password", JToken.FromObject(_hsRobots[i].Password));
						robotData.Add("stateString", JToken.FromObject(_hsRobots[i].StateString));
						robotData.Add("ip", JToken.FromObject(_hsRobots[i].ConnectedIp));
						robotData.Add("type", JToken.FromObject(_hsRobots[i].Type == RobotType.Vacuum ? "vacuum" : "mop"));
						robotData.Add("name", JToken.FromObject(_hsRobots[i].Robot?.Name ?? "Unknown"));
						robotData.Add("sku", JToken.FromObject(_hsRobots[i].Robot?.Sku ?? "unknown"));
						robots[i] = robotData;
					}
					
					return JsonConvert.SerializeObject(new { robots });
				
				case "getRobotFullStatus":
					blid = (string) payload.SelectToken("blid");
					if (blid == null) {
						return badCmdResponse;
					}

					HsRobot robot = _hsRobots.Find(bot => bot.Blid == blid);
					return robot == null
						? JsonConvert.SerializeObject(new { error = "Invalid blid" })
						: JsonConvert.SerializeObject(new { status = robot.Robot.GetFullStatus() });

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

			PlugExtraData versionExtraData = new PlugExtraData();
			versionExtraData.AddNamed("version", "1");
			
			DeviceFactory factory = DeviceFactory.CreateDevice(Id)
				.WithName(verifier.Name)
				.WithAddress(blid)
				.WithExtraData(extraData)
				.WithFeature(FeatureFactory.CreateFeature(Id)
					.WithName("Status")
					.WithAddress($"{blid}:Status")
					.WithExtraData(versionExtraData)
					.WithDisplayType(EFeatureDisplayType.Important)
					.AddGraphicForValue("/images/HomeSeer/status/off.gif", (double) RobotStatus.OnBase, "On Home Base")
					.AddGraphicForValue("/images/HomeSeer/status/on.gif", (double) RobotStatus.Clean, "Cleaning")
					.AddGraphicForValue("/images/HomeSeer/status/pause.png", (double) RobotStatus.JobPaused, "Job Paused")
					.AddGraphicForValue("/images/HomeSeer/status/stop.png", (double) RobotStatus.OffBaseNoJob, "Off Base")
					.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) RobotStatus.Stuck, "Stuck")
					.AddGraphicForValue("/images/HomeSeer/status/home.png", (double) RobotStatus.DockManually, "Returning To Home Base")
					.AddGraphicForValue("/images/HomeSeer/status/eject.png", (double) RobotStatus.Evac, "Emptying Bin")
					.AddGraphicForValue("/images/HomeSeer/status/zoom.png", (double) RobotStatus.Train, "Mapping Run")
					.AddButton((double) RobotStatus.Clean, "Clean")
					.AddButton((double) RobotStatus.JobPaused, "Pause")
					.AddButton((double) RobotStatus.Resume, "Resume")
					.AddButton((double) RobotStatus.OffBaseNoJob, "Abort Job")
					.AddButton((double) RobotStatus.DockManually, "Return To Home Base")
					.AddButton((double) RobotStatus.Find, "Locate")
				)
				.WithFeature(FeatureFactory.CreateFeature(Id)
					.WithName("Job Phase")
					.WithAddress($"{blid}:JobPhase")
					.WithExtraData(versionExtraData)
					.AddGraphicForValue("/images/HomeSeer/status/off.gif", (double) CleanJobPhase.NoJob, "No Job")
					.AddGraphicForValue("/images/HomeSeer/status/play.png", (double) CleanJobPhase.Cleaning, "Cleaning")
					.AddGraphicForValue("/images/HomeSeer/status/electricity.gif", (double) CleanJobPhase.Charging, "Charging")
					.AddGraphicForValue("/images/HomeSeer/status/eject.png", (double) CleanJobPhase.Evac, "Emptying Bin")
					.AddGraphicForValue("/images/HomeSeer/status/batterytoolowtooperatelock.png", (double) CleanJobPhase.LowBatteryReturningToDock, "Returning to Home Base to Recharge")
					.AddGraphicForValue("/images/HomeSeer/status/replay.png", (double) CleanJobPhase.DoneReturningToDock, "Finished, Returning To Home Base")
				)
				.WithFeature(FeatureFactory.CreateFeature(Id)
					.WithName("Battery")
					.WithAddress($"{blid}:Battery")
					.WithExtraData(versionExtraData)
					.AddGraphicForRange("/images/HomeSeer/status/battery_0.png", 0, 10)
					.AddGraphicForRange("/images/HomeSeer/status/battery_25.png", 11, 37)
					.AddGraphicForRange("/images/HomeSeer/status/battery_50.png", 38, 63)
					.AddGraphicForRange("/images/HomeSeer/status/battery_75.png", 64, 88)
					.AddGraphicForRange("/images/HomeSeer/status/battery_100.png", 89, 100)
					.WithDisplayType(EFeatureDisplayType.Important)
				);

			switch (verifier.DetectedType) {
				case RobotType.Vacuum:
					factory.WithFeature(FeatureFactory.CreateFeature(Id)
						.WithName("Bin")
						.WithAddress($"{blid}:VacuumBin")
						.WithExtraData(versionExtraData)
						.AddGraphicForValue("/images/HomeSeer/status/ok.png", (double) BinStatus.Ok, "OK")
						.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) BinStatus.Full, "Full")
						.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) BinStatus.NotPresent, "Removed")
					);
					break;
				
				case RobotType.Mop:
					factory.WithFeature(FeatureFactory.CreateFeature(Id)
							.WithName("Tank")
							.WithAddress($"{blid}:MopTank")
							.WithExtraData(versionExtraData)
							.AddGraphicForValue("/images/HomeSeer/status/ok.png", (double) TankStatus.Ok, "OK")
							.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) TankStatus.Empty, "Empty")
							.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) TankStatus.LidOpen, "Lid Open")
						)
						.WithFeature(FeatureFactory.CreateFeature(Id)
							.WithName("Pad")
							.WithAddress($"{blid}:MopPad")
							.WithExtraData(versionExtraData)
							.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) MopPadType.Invalid, "None")
							.AddGraphicForValue("/images/HomeSeer/status/water.gif", (double) MopPadType.DisposableWet, "Disposable Wet")
							.AddGraphicForValue("/images/HomeSeer/status/luminance-00.png", (double) MopPadType.DisposableDry, "Disposable Dry")
							.AddGraphicForValue("/images/HomeSeer/status/water.gif", (double) MopPadType.ReusableWet, "Reusable Wet")
							.AddGraphicForValue("/images/HomeSeer/status/luminance-00.png", (double) MopPadType.ReusableDry, "Reusable Dry")
						);
					break;
			}

			factory.WithFeature(FeatureFactory.CreateFeature(Id)
					.WithName("Readiness")
					.WithAddress($"{blid}:Ready")
					.WithExtraData(versionExtraData)
					.AddGraphicForValue("/images/HomeSeer/status/ok.png", 0, "Ready")
					.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 1, "Near a cliff")
					.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 2, "Both wheels dropped")
					.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 3, "Left wheel dropped")
					.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 4, "Right wheel dropped")
					.AddGraphicForRange("/images/HomeSeer/status/alarm.png", 5, 6, "Not Ready")
					.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 7, "Insert the bin")
					.AddGraphicForRange("/images/HomeSeer/status/alarm.png", 8, 14, "Not Ready")
					.AddGraphicForValue("/images/HomeSeer/status/batterytoolowtooperatelock.png", 15, "Low battery")
					.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 16, "Empty the bin")
					.AddGraphicForRange("/images/HomeSeer/status/alarm.png", 17, 30, "Not Ready")
					.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 31, "Fill the tank")
					.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 32, "Close the lid")
					.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 33, "Not Ready")
					.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 34, "Attach a pad")
					.AddGraphicForRange("/images/HomeSeer/status/alarm.png", 35, 38, "Not Ready")
					.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 39, "Saving clean map")
					.AddGraphicForRange("/images/HomeSeer/status/alarm.png", 40, 255, "Not Ready")
				)
				.WithFeature(FeatureFactory.CreateFeature(Id)
					.WithName("Error")
					.WithAddress($"{blid}:Error")
					.WithExtraData(versionExtraData)
					.AddGraphicForValue("/images/HomeSeer/status/ok.png", 0, "No Error")
					.AddGraphicForRange("/images/HomeSeer/status/alarm.png", 1, 255)
				);

			int newDeviceRef = HomeSeerSystem.CreateDevice(factory.PrepareForHs());
			WriteLog(ELogType.Info, $"Created new device {newDeviceRef} for {verifier.DetectedType} robot {verifier.Name} ({blid})");

			if (verifier.DetectedType == RobotType.Vacuum) {
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

			HsRobot robot = new HsRobot(this, HomeSeerSystem.GetDeviceByRef(newDeviceRef));
			robot.OnRobotStatusUpdated += HandleRobotStatusUpdate;
			await robot.AttemptConnect();

			_hsRobots.Add(robot);
		}

		public IHsController GetHsController() {
			return HomeSeerSystem;
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
	}
}