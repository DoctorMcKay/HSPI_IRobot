using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Events;
using HomeSeer.PluginSdk.Logging;
using Newtonsoft.Json.Linq;

namespace HSPI_IRobot.HsEvents {
	public class RobotTrigger : AbstractTriggerType {
		public const int TriggerNumber = 1;
		
		protected override List<string> SubTriggerTypeNames { get; set; } = new List<string> {
			"A robot begins/is navigating the space",
			"A robot stops/is not navigating the space"
		};

		private string OptionIdExplainTrigger => $"{PageId}-ExplainTrig";
		private string OptionIdExplainCondition => $"{PageId}-ExplainCond";
		private string OptionIdRobot => $"{PageId}-Robot";
		private HSPI Plugin => (HSPI) TriggerListener;
		
		protected override string GetName() => "iRobot: A robot is...";
		public override bool CanBeCondition => true;

		private SubTrigger? SubTrig {
			get {
				if (SelectedSubTriggerIndex < 0) {
					return null;
				}

				if (!Enum.IsDefined(typeof(SubTrigger), SelectedSubTriggerIndex)) {
					return null;
				}

				return (SubTrigger) SelectedSubTriggerIndex;
			}
		}

		public RobotTrigger(TrigActInfo trigInfo, TriggerTypeCollection.ITriggerTypeListener listener, bool logDebug = false) : base(trigInfo, listener, logDebug) { }

		public RobotTrigger(int id, int eventRef, int selectedSubTriggerIndex, byte[] dataIn, TriggerTypeCollection.ITriggerTypeListener listener, bool logDebug = false) : base(id, eventRef, selectedSubTriggerIndex, dataIn, listener, logDebug) { }
		public RobotTrigger() { }

		protected override void OnNewTrigger() {
			if (SelectedSubTriggerIndex < 0) {
				// No sub-trigger selected yet
				return;
			}
			
			List<KeyValuePair<string, string>> robots = Plugin.HsRobots.Select(robot => new KeyValuePair<string, string>(robot.Blid, robot.GetName())).ToList();
			robots.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.Ordinal));
			List<string> robotIds = robots.Select(robot => robot.Key).ToList();
			List<string> robotNames = robots.Select(robot => robot.Value).ToList();

			PageFactory factory = PageFactory.CreateEventTriggerPage(PageId, Name);
			
			// Add a LabelView to explain how this trigger works because we can't detect whether it's a trigger or a condition
			switch (SubTrig) {
				case SubTrigger.IsNavigating:
					factory.WithLabel(OptionIdExplainTrigger, "When used as a trigger (IF/OR IF)", "Triggers the event when the robot begins driving around your space");
					factory.WithLabel(OptionIdExplainCondition, "When used as a condition (AND IF)", "Passes when the robot is currently driving around your space");
					break;
				
				case SubTrigger.IsNotNavigating:
					factory.WithLabel(OptionIdExplainTrigger, "When used as a trigger (IF/OR IF)", "Triggers the event when the robot stops driving around your space");
					factory.WithLabel(OptionIdExplainCondition, "When used as a condition (AND IF)", "Passes when the robot is not currently driving around your space");
					break;
			}

			factory.WithDropDownSelectList(OptionIdRobot, "Robot", robotNames, robotIds);
			ConfigPage = factory.Page;
		}

		public override bool IsFullyConfigured() {
			return _getRobotBlid() != null;
		}

		protected override bool OnConfigItemUpdate(AbstractView configViewChange) {
			return true;
		}

		public override string GetPrettyString() {
			HsRobot robot = _getRobot();
			if (robot == null) {
				return "";
			}

			string triggerName = SubTriggerTypeNames[SelectedSubTriggerIndex].Substring("A robot ".Length);
			
			bool? isCondition = _triggerIsCondition();
			if (isCondition != null) {
				bool isTrigger = !(bool) isCondition;
				
				switch (SubTrig) {
					case SubTrigger.IsNavigating:
						triggerName = isTrigger ? "begins navigating the space" : "is navigating the space";
						break;
					
					case SubTrigger.IsNotNavigating:
						triggerName = isTrigger ? "stops navigating the space" : "is not navigating the space";
						break;
				}
			}
			
			return $"iRobot: <font class=\"event_Txt_Option\">{robot.GetName()}</font> <font class=\"event_Txt_Selection\">{triggerName}</font>";
		}

		public override bool IsTriggerTrue(bool isCondition) {
			HsRobot robot = _getRobot();

			switch (SubTrig) {
				case SubTrigger.IsNavigating:
					// Is navigating
					return robot.IsNavigating();
				
				case SubTrigger.IsNotNavigating:
					// Is not navigating
					return !robot.IsNavigating();
				
				default:
					Plugin.WriteLog(ELogType.Warning, $"Got IsTriggerTrue call for unknown sub-trigger {SelectedSubTriggerIndex}");
					return false;
			}
		}

		public override bool ReferencesDeviceOrFeature(int devOrFeatRef) {
			string blid = _getRobotBlid();
			if (blid == null) {
				return false;
			}
			
			string address = (string) Plugin.GetHsController().GetPropertyByRef(devOrFeatRef, EProperty.Address);
			return address.StartsWith(blid);
		}

		private string _getRobotBlid() {
			if (ConfigPage == null || !ConfigPage.ContainsViewWithId(OptionIdRobot)) {
				return null;
			}

			string blid = ConfigPage.GetViewById<SelectListView>(OptionIdRobot).GetSelectedOptionKey();
			return string.IsNullOrEmpty(blid) ? null : blid;
		}

		private HsRobot _getRobot() {
			string blid = _getRobotBlid();
			if (blid == null) {
				return null;
			}

			return Plugin.HsRobots.Find(r => r.Blid == blid);
		}

		private bool? _triggerIsCondition() {
			// This is the best we can do until this is added to the plugin SDK proper.
			// It's not going to be perfect.
			// See: https://github.com/HomeSeer/Plugin-SDK/issues/237

			string appPath = Plugin.GetHsController().GetAppPath();
			FileStream eventsJsonFile;

			try {
				eventsJsonFile = File.OpenRead(Path.Combine(appPath, "Data", "HomeSeerData.json", "events.json"));
			} catch (FileNotFoundException) {
				// Maybe we're running remotely
				return null;
			}

			JToken eventObj = null;
			
			using (eventsJsonFile) {
				byte[] buffer = new byte[eventsJsonFile.Length];
				int offset = 0;
				while (offset < eventsJsonFile.Length) {
					offset += eventsJsonFile.Read(buffer, offset, (int) eventsJsonFile.Length - offset);
				}
				
				JArray eventsArray = JArray.Parse(Encoding.UTF8.GetString(buffer));
				eventObj = eventsArray.ToList().Find(obj => (int) obj.SelectToken("evRef") == EventRef);
			}

			// Loop through all TrigGroups and see if this trigger is first in a group
			JObject trigGroups = eventObj?.SelectToken("Triggers.TrigGroups")?.Value<JObject>();
			if (trigGroups == null) {
				return null;
			}

			int trigGroupCount = Plugin.GetHsController().GetEventByRef(EventRef).Trigger_Groups.Length;
			if (trigGroupCount != trigGroups.Count) {
				// The json file hasn't been saved to disk yet, so we're dealing with old data
				return null;
			}

			foreach (JProperty prop in trigGroups.Properties()) {
				JArray triggers = prop.Value.SelectToken("$values")?.Value<JArray>();
				if (triggers == null) {
					continue;
				}

				List<JToken> triggerList = triggers.ToList();
				JToken trigger = triggerList.Find(trig => (int) trig.SelectToken("mvarTI.UID") == Id);
				if (trigger == null) {
					continue;
				}

				int triggerIdx = triggerList.IndexOf(trigger);
				return triggerIdx > 0; // it's a condition if it's not the first trigger in the group
			}

			return null;
		}

		private enum SubTrigger : int {
			IsNavigating = 0,
			IsNotNavigating
		}
	}
}