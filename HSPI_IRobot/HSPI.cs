using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Devices.Controls;
using HomeSeer.PluginSdk.Events;
using HomeSeer.PluginSdk.Logging;
using HSPI_IRobot.Enums;
using HSPI_IRobot.FeaturePageHandlers;
using HSPI_IRobot.HsEvents;
using IRobotLANClient;
using IRobotLANClient.Enums;
using IRobotLANClient.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HSPI_IRobot {
	public class HSPI : AbstractPlugin {
		public static HSPI Instance { get; private set; }
		
		public const string PluginId = "iRobot";
		public override string Name { get; } = "iRobot";
		public override string Id { get; } = PluginId;
		public override bool SupportsConfigDevice { get; } = true;

		private bool _debugLogging;

		internal List<HsRobot> HsRobots;
		private AnalyticsClient _analyticsClient;

		public HSPI() {
			Instance = this;
		}

		protected override void Initialize() {
			string pluginVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
			string irobotClientVersion = FileVersionInfo.GetVersionInfo(Assembly.GetAssembly(typeof(RobotClient)).Location).FileVersion;

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
			HomeSeerSystem.RegisterFeaturePage(Id, "favorites.html", "Favorite Jobs");
			
			// Initialize our device list
			InitializeDeviceList();
			
			// Set up event triggers and actions
			TriggerTypes.AddTriggerType(typeof(RobotTrigger));
			ActionTypes.AddActionType(typeof(RobotAction));
			
			_analyticsClient.ReportIn(5000);
		}

		private async void InitializeDeviceList() {
			HsRobots = new List<HsRobot>();

			await Task.WhenAll(HomeSeerSystem.GetRefsByInterface(Id, true).Select(deviceRef => Task.Run(async () => {
				await InitializeDevice(deviceRef);
			})).ToList());

			int countRobots = HsRobots.Count;
			int connectedRobots = HsRobots.FindAll(robot => robot.State == HsRobot.HsRobotState.Connected).Count;
			WriteLog(ELogType.Info, $"All devices initialized. Found {countRobots} robots and {connectedRobots} connected successfully");
		}

		private async Task InitializeDevice(int deviceRef) {
			HsRobot robot = new HsRobot(deviceRef);
			HsRobots.Add(robot);

			robot.OnConnectStateUpdated += HandleRobotConnectionStateUpdate;
			robot.OnRobotStatusUpdated += HandleRobotStatusUpdate;

			await robot.AttemptConnect();
			WriteLog(ELogType.Debug, $"Initial connection attempt to {robot.GetName()} finished with new state {robot.State} ({robot.StateString})");

			if (robot.State == HsRobot.HsRobotState.Connected) {
				PlugExtraData ped = robot.PlugExtraData;
				if (!ped.ContainsNamed("lastknownip")) {
					ped.AddNamed("lastknownip", robot.ConnectedIp);
				} else {
					ped["lastknownip"] = robot.ConnectedIp;
				}

				robot.PlugExtraData = ped;
			}
		}

		public override void SetIOMulti(List<ControlEvent> colSend) {
			foreach (ControlEvent ctrl in colSend) {
				HsFeature feature = HomeSeerSystem.GetFeatureByRef(ctrl.TargetRef);
				string[] addressParts = feature.Address.Split(':');
				HsRobot robot = HsRobots.Find(r => r.Blid == addressParts[0]);
				if (robot?.Client == null) {
					WriteLog(ELogType.Warning, $"Got SetIOMulti {ctrl.TargetRef} = {ctrl.ControlValue}, but no such robot found or not connected");
					continue;
				}

				RobotStatus command = (RobotStatus) ctrl.ControlValue;
				switch (command) {
					case RobotStatus.Clean:
						robot.Client.Clean();
						break;
					
					case RobotStatus.OffBaseNoJob:
						robot.Client.Stop();
						break;
					
					case RobotStatus.JobPaused:
						robot.Client.Pause();
						break;
					
					case RobotStatus.Resume:
						robot.Client.Resume();
						break;
					
					case RobotStatus.DockManually:
						robot.Client.Dock();
						break;
					
					case RobotStatus.Find:
						robot.Client.Find();
						break;
					
					case RobotStatus.Evac:
						if (robot.Type == RobotType.Vacuum) {
							RobotVacuumClient roboVac = (RobotVacuumClient) robot.Client;
							roboVac.Evac();
						}

						break;
					
					case RobotStatus.Train:
						robot.Client.Train();
						break;
				}
			}
		}

		private void HandleRobotConnectionStateUpdate(object src, EventArgs arg) {
			HsRobot robot = (HsRobot) src;
			HsFeature errorFeature = robot.GetFeature(FeatureType.Error);
			
			InternalError newInternalError;
			string newErrorString = "";

			switch (robot.State) {
				case HsRobot.HsRobotState.Connected:
					newInternalError = InternalError.None;
					break;
				
				case HsRobot.HsRobotState.Disconnected:
					newInternalError = InternalError.DisconnectedFromRobot;
					break;
				
				case HsRobot.HsRobotState.CannotConnect:
					switch (robot.CannotConnectReason) {
						case HsRobot.HsRobotCannotConnectReason.CannotDiscover:
							newInternalError = InternalError.CannotDiscoverRobot;
							break;
						
						case HsRobot.HsRobotCannotConnectReason.DiscoveredCannotConnect:
						case HsRobot.HsRobotCannotConnectReason.CannotValidateType:
							newInternalError = InternalError.CannotConnectToMqtt;
							break;
						
						case HsRobot.HsRobotCannotConnectReason.ConnectionDisabled:
							newInternalError = InternalError.ConnectionDisabled;
							if (robot.StateString != "Connection disabled") {
								newErrorString = robot.StateString;
							}

							break;
						
						default:
							newInternalError = InternalError.DisconnectedFromRobot;
							break;
					}

					break;
				
				case HsRobot.HsRobotState.FatalError:
					newInternalError = InternalError.DisconnectedFromRobot;
					newErrorString = "Fatal Error";
					break;
				
				case HsRobot.HsRobotState.Connecting:
				default:
					// We don't want to update anything when we're reconnecting
					return;
			}

			HomeSeerSystem.UpdateFeatureValueByRef(errorFeature.Ref, (double) newInternalError);
			HomeSeerSystem.UpdateFeatureValueStringByRef(errorFeature.Ref, newErrorString);
		}

		private void HandleRobotStatusUpdate(object src, EventArgs arg) {
			HsRobot robot = (HsRobot) src;
			WriteLog(ELogType.Debug, $"Robot {robot.Client.Name} updated: battery {robot.Client.BatteryLevel}; cycle {robot.Client.Cycle}; phase {robot.Client.Phase}");

			bool wasNavigating = robot.IsNavigating();

			// Features common to all robot types
			// Status
			RobotStatus status = RobotStatus.OffBaseNoJob;
			switch (robot.Client.Cycle) {
				case MissionCycle.None:
					status = robot.Client.Phase == MissionPhase.Stop ? RobotStatus.OffBaseNoJob : RobotStatus.OnBase;
					break;
				
				case MissionCycle.Clean:
				case MissionCycle.Spot:
					switch (robot.Client.Phase) {
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

			if (robot.Client.Phase == MissionPhase.Stuck) {
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
					switch (robot.Client.Phase) {
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
			bool isCharging = robot.Client.BatteryLevel < 100 && robot.Client.Phase == MissionPhase.Charge;
			feature = robot.GetFeature(FeatureType.Battery);
			HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, robot.Client.BatteryLevel);
			HomeSeerSystem.UpdateFeatureValueStringByRef(
				feature.Ref,
				isCharging ? $"{robot.Client.BatteryLevel}% (charging)" : ""
			);
			
			// Ready
			feature = robot.GetFeature(FeatureType.Ready);
			HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, robot.Client.NotReadyCode);
			
			// Error
			feature = robot.GetFeature(FeatureType.Error);
			HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, robot.Client.ErrorCode);

			switch (robot.Type) {
				case RobotType.Vacuum:
					RobotVacuumClient roboVac = (RobotVacuumClient) robot.Client;
					
					// Bin
					feature = robot.GetFeature(FeatureType.VacuumBin);
					HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, (double) roboVac.BinStatus);
					
					break;
				
				case RobotType.Mop:
					RobotMopClient roboMop = (RobotMopClient) robot.Client;
					
					// Tank
					feature = robot.GetFeature(FeatureType.MopTank);
					HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, (double) roboMop.TankStatus);
					
					// Pad type
					feature = robot.GetFeature(FeatureType.MopPad);
					HomeSeerSystem.UpdateFeatureValueByRef(feature.Ref, (double) roboMop.MopPadType);
					
					break;
			}
			
			// Did our navigating state change?
			if (robot.IsNavigating() != wasNavigating) {
				WriteLog(ELogType.Debug, $"{robot.GetName()} navigating status changed: {wasNavigating} -> {!wasNavigating}");
				foreach (TrigActInfo trigActInfo in HomeSeerSystem.GetTriggersByType(Id, RobotTrigger.TriggerNumber)) {
					RobotTrigger trigger = new RobotTrigger(trigActInfo, this, _debugLogging);
					if (
						(trigger.SubTrig == RobotTrigger.SubTrigger.IsNavigating || trigger.SubTrig == RobotTrigger.SubTrigger.IsNotNavigating)
						&& trigger.ReferencesDeviceOrFeature(robot.HsDeviceRef)
						&& trigger.IsTriggerTrue(false)
					) {
						HomeSeerSystem.TriggerFire(Id, trigActInfo);
					}
				}
			}
			
			// Are we now downloading a software update?
			if (robot.Client.SoftwareUpdateDownloadProgress > 0 && !robot.ObservedSoftwareUpdateDownload) {
				robot.ObservedSoftwareUpdateDownload = true;
				WriteLog(ELogType.Info, $"{robot.GetName()} is now downloading a software update");
				foreach (TrigActInfo trigActInfo in HomeSeerSystem.GetTriggersByType(Id, RobotTrigger.TriggerNumber)) {
					RobotTrigger trigger = new RobotTrigger(trigActInfo, this, _debugLogging);
					if (
						trigger.SubTrig == RobotTrigger.SubTrigger.IsDownloadingUpdate
						&& trigger.ReferencesDeviceOrFeature(robot.HsDeviceRef)
						&& trigger.IsTriggerTrue(false)
					) {
						HomeSeerSystem.TriggerFire(Id, trigActInfo);
					}
				}
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

		public override bool HasJuiDeviceConfigPage(int deviceRef) {
			return HsRobots.Exists(r => r.HsDeviceRef == deviceRef);
		}

		public override string GetJuiDeviceConfigPage(int deviceRef) {
			HsRobot robot = HsRobots.Find(r => r.HsDeviceRef == deviceRef);

			PageFactory factory = PageFactory.CreateDeviceConfigPage("iRobotDevice", "iRobot")
				.WithLabel("Status", "Status", robot.StateString)
				.WithLabel("IPAddress", "Address", string.IsNullOrEmpty(robot.ConnectedIp) ? "unknown" : robot.ConnectedIp)
				.WithLabel("BLID", "BLID", robot.Blid)
				.WithLabel("SKU", "SKU", robot.Client?.Sku ?? "unknown")
				.WithLabel("Type", "Robot Type", Enum.GetName(typeof(RobotType), robot.Type))
				.WithLabel("SoftwareVersion", "Software Version", robot.Client?.SoftwareVersion ?? "unknown");
			
			// If we're connected, add settings
			if (robot.State == HsRobot.HsRobotState.Connected && robot.Client != null) {
				foreach (ConfigOption option in Enum.GetValues(typeof(ConfigOption))) {
					if (!robot.Client.SupportsConfigOption(option)) {
						continue;
					}

					List<string> keys = RobotOptions.GetOptionKeys(option);
					List<string> labels = RobotOptions.GetOptionLabels(option);
					string currentSetting;
					switch (option) {
						case ConfigOption.ChargeLightRingPattern:
							currentSetting = ((int) robot.Client.ChargeLightRingPattern).ToString();
							break;
						
						case ConfigOption.ChildLock:
							currentSetting = robot.Client.ChildLock ? "1" : "0";
							break;
						
						case ConfigOption.BinFullPause:
							currentSetting = ((RobotVacuumClient) robot.Client).BinFullPause ? "1" : "0";
							break;
						
						case ConfigOption.CleaningPassMode:
							currentSetting = ((int) ((RobotVacuumClient) robot.Client).CleaningPassMode).ToString();
							break;
						
						case ConfigOption.WetMopPadWetness:
							currentSetting = ((RobotMopClient) robot.Client).WetMopPadWetness.ToString();
							break;
						
						case ConfigOption.WetMopPassOverlap:
							currentSetting = ((RobotMopClient) robot.Client).WetMopRankOverlap.ToString();
							break;
						
						case ConfigOption.EvacAllowed:
							currentSetting = ((RobotVacuumClient) robot.Client).EvacAllowed ? "1" : "0";
							break;
						
						default:
							continue;
					}
					
					factory.WithDropDownSelectList(
						Enum.GetName(typeof(ConfigOption), option),
						RobotOptions.GetOptionName(option),
						labels,
						keys,
						keys.IndexOf(currentSetting)
					);
				}
				
				#if DEBUG_CLIENT
				factory.WithToggle("SpoofSoftwareUpdate", "Spoof Software Update");
				#endif
			}

			factory.WithLabel("ManageRobots", "", "<hr /><a href=\"/iRobot/robots.html\">Manage Robots</a>");
			return factory.Page.ToJsonString();
		}

		protected override bool OnDeviceConfigChange(Page changedPage, int deviceOrFeatureRef) {
			HsRobot robot = HsRobots.Find(r => r.HsDeviceRef == deviceOrFeatureRef);
			if (robot == null) {
				return false;
			}

			if (robot.Client == null || robot.State != HsRobot.HsRobotState.Connected) {
				throw new Exception("Robot is not currently connected");
			}

			foreach (AbstractView view in changedPage.Views) {
				#if DEBUG_CLIENT
				if (view.Id == "SpoofSoftwareUpdate" && view is ToggleView toggleView && toggleView.IsEnabled) {
					robot.Client.SpoofSoftwareUpdate();
					continue;
				}
				#endif
				
				if (!(view is SelectListView listView)) {
					throw new Exception($"View {view.Id} is not a SelectListView");
				}

				if (!Enum.TryParse(listView.Id, out ConfigOption option)) {
					throw new Exception($"View ID {listView.Id} is not a valid ConfigOption");
				}

				if (listView.Selection == -1) {
					throw new Exception($"Cannot unset setting value {listView.Id}");
				}
				
				string newValue = RobotOptions.GetOptionKeys(option)[listView.Selection];
				WriteLog(ELogType.Trace, $"Requested to change {option} to {newValue} on {robot.GetName()}");

				if (!int.TryParse(newValue, out int settingValue)) {
					throw new Exception("Setting value is not an integer");
				}

				if (!RobotOptions.ChangeSetting(robot, option, settingValue)) {
					return false;
				}
			}
			
			// Sleep for a second so we don't refresh and see an old value
			Thread.Sleep(1000);
			return true;
		}

		protected override void BeforeReturnStatus() {
			
		}

		public override string PostBackProc(string page, string data, string user, int userRights) {
			//WriteLog(ELogType.Trace, $"PostBackProc page name {page} by user {user} with rights {userRights}");
			
			if ((userRights & 2) != 2) {
				return JsonConvert.SerializeObject(new {
					error = "Access Denied",
					fatal = true
				});
			}

			try {
				AbstractFeaturePageHandler handler = AbstractFeaturePageHandler.GetHandler(page);
				return handler.PostBackProc(data, user);
			} catch (Exception ex) {
				WriteLog(ELogType.Warning, $"PostBackProc error for page {page}: {ex.Message}");
				return ex.Message;
			}
		}

		public async Task<string> AddNewRobot(string ip, string blid, string password) {
			// First things first, let's try to connect and see if we can
			RobotVerifierClient verifier = null;
			try {
				WriteLog(ELogType.Debug, $"Adding new robot with IP {ip} and BLID {blid}");

				verifier = new RobotVerifierClient(ip, blid, password);
				verifier.OnDebugOutput += (sender, args) => WriteLog(ELogType.Debug, $"[V:{blid}] {args.Output}");
				await verifier.Connect();

				WriteLog(ELogType.Debug, $"Verifier for {blid} connected");

				RobotType robotType = await verifier.WaitForDetectedType();
				WriteLog(ELogType.Debug, $"Verifier for {blid} detected type as {robotType}");
				await verifier.Disconnect();
				WriteLog(ELogType.Debug, $"Disconnected from {blid} verifier");

				if (robotType == RobotType.Unrecognized) {
					WriteLog(ELogType.Debug, "Unrecognized robot type");
					WriteLog(ELogType.Debug, JsonConvert.SerializeObject(verifier.ReportedState));
					return "Unrecognized robot type";
				}

				OneShotTimer timer = new OneShotTimer(1000);
				timer.Elapsed += (sender, args) => _createNewRobotDevice(ip, blid, password, verifier);

				return "OK";
			} catch (TaskCanceledException) {
				if (verifier != null && verifier.Connected) {
					await verifier.Disconnect();
				}

				WriteLog(ELogType.Error, $"Robot verification timed out for {blid}");
				WriteLog(ELogType.Debug, JsonConvert.SerializeObject(verifier?.ReportedState));
				return "Robot verification timed out";
			} catch (RobotConnectionException ex) {
				DiscoveryClient.DiscoveredRobot robotMetadata = await new DiscoveryClient().GetRobotPublicDetails(ip);
				if (robotMetadata == null) {
					return "This IP address doesn't appear to belong to an iRobot product.";
				}

				if (robotMetadata.Blid != blid) {
					return $"Provided BLID does not belong to the robot at IP address {ip}. This robot's BLID is {robotMetadata.Blid}.";
				}
				
				switch (ex.ConnectionError) {
					case ConnectionError.ConnectionRefused:
						// Make sure that this is actually the robot we think it is
						return "Another app is already connected to this robot. Make sure that the iRobot Home app is " +
						       "fully closed on your mobile device(s) and that no other home automation plugins for " +
						       "iRobot devices are running.";

					case ConnectionError.ConnectionTimedOut:
						return "Connection timed out. Make sure the IP address is correct.";
					
					default:
						return ex.FriendlyMessage;
				}
			} catch (Exception ex) {
				// Failed to connect
				return ex.Message;
			}
		}

		private async void _createNewRobotDevice(string ip, string blid, string password, RobotVerifierClient verifier) {
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
				// Not all vacuums can self-empty their bins, but definitely no mops can so this is good enough.
				// The presence of evacAllowed in the robot's status data likely indicates whether it can self-empty,
				// but I'm not 100% sure of what happens if an i/j/s series robot is initially set up without a clean base
				// and one is added later. If evacAllowed is always present for those models, then it would be a good
				// way to determine if an Empty Bin status control is useful, but if it only appears once a clean base
				// is in the picture, then we'd need to account for adding the control later. For now, let's just go ahead
				// and put the control on all vacuums.
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

			await InitializeDevice(newDeviceRef);
		}

		public AnalyticsClient GetAnalyticsClient() {
			return _analyticsClient;
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

		public void BackupPlugExtraData(int deviceRef) {
			PlugExtraData ped = (PlugExtraData) HomeSeerSystem.GetPropertyByRef(deviceRef, EProperty.PlugExtraData);
			Dictionary<string, string> backup = ped.NamedKeys.ToDictionary(key => key, key => ped[key]);
			HomeSeerSystem.SaveINISetting("PED_Backup", deviceRef.ToString(), JsonConvert.SerializeObject(backup), SettingsFileName);
		}

		public bool RestorePlugExtraData(int deviceRef) {
			string jsonPayload = HomeSeerSystem.GetINISetting("PED_Backup", deviceRef.ToString(), string.Empty, SettingsFileName);
			if (string.IsNullOrEmpty(jsonPayload)) {
				return false;
			}

			PlugExtraData ped = new PlugExtraData();
			JObject payload = JObject.Parse(jsonPayload);
			foreach (JProperty prop in payload.Properties()) {
				ped.AddNamed(prop.Name, (string) prop.Value);
			}

			HomeSeerSystem.UpdatePropertyByRef(deviceRef, EProperty.PlugExtraData, ped);
			return true;
		}

		public void WriteLog(ELogType logType, string message, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string caller = null) {
			_analyticsClient?.WriteLog(logType, message, lineNumber, caller);
			
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