using System;
using System.Collections.Generic;
using System.Linq;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Events;
using HomeSeer.PluginSdk.Logging;

namespace HSPI_IRobot.HsEvents {
	public class RobotTrigger : AbstractTriggerType {
		public const int TriggerNumber = 1;
		
		protected override List<string> SubTriggerTypeNames { get; set; } = new List<string> {
			"A robot begins/is navigating the space",
			"A robot stops/is not navigating the space"
		};

		private string OptionIdRobot => $"{PageId}-Robot";
		private HSPI Plugin => (HSPI) TriggerListener;
		
		protected override string GetName() => "iRobot: A robot is...";
		public override bool CanBeCondition => true;

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
			return $"iRobot: <font class=\"event_Txt_Option\">{robot.GetName()}</font> <font class=\"event_Txt_Selection\">{triggerName}</font>";
		}

		public override bool IsTriggerTrue(bool isCondition) {
			HsRobot robot = _getRobot();

			switch (SelectedSubTriggerIndex) {
				case 0:
					// Is navigating
					return robot.IsNavigating();
				
				case 1:
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
	}
}