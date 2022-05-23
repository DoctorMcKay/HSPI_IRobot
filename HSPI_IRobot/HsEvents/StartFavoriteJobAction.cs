using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Events;
using HSPI_IRobot.FeaturePageHandlers;

namespace HSPI_IRobot.HsEvents {
	public class StartFavoriteJobAction : AbstractActionType {
		private string OptionIdRobot => $"{PageId}-Robot";
		private string OptionIdJob => $"{PageId}-JobName";
		private HSPI Plugin => (HSPI) ActionListener;

		public StartFavoriteJobAction() { }
		public StartFavoriteJobAction(int id, int eventRef, byte[] dataIn, ActionTypeCollection.IActionTypeListener listener, bool logDebug = false) : base(id, eventRef, dataIn, listener, logDebug) { }

		protected override string GetName() => "iRobot: Start Favorite Job";

		protected override void OnNewAction() {
			List<KeyValuePair<string, string>> robots = Plugin.HsRobots.Select(robot => new KeyValuePair<string, string>(robot.Blid, robot.GetName())).ToList();
            robots.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.Ordinal));
            List<string> robotIds = robots.Select(robot => robot.Key).ToList();
            List<string> robotNames = robots.Select(robot => robot.Value).ToList();

            PageFactory factory = PageFactory.CreateEventActionPage(PageId, Name);
            factory.WithDropDownSelectList(OptionIdRobot, "Robot", robotNames, robotIds);
            ConfigPage = factory.Page;
		}

		public override bool IsFullyConfigured() {
			return !string.IsNullOrWhiteSpace(ConfigPage.GetViewById<SelectListView>(OptionIdRobot).GetSelectedOptionKey())
			       && ConfigPage.ContainsViewWithId(OptionIdJob)
			       && ConfigPage.GetViewById(OptionIdJob) is SelectListView
			       && !string.IsNullOrWhiteSpace(ConfigPage.GetViewById<SelectListView>(OptionIdJob).GetSelectedOption());
		}

		protected override bool OnConfigItemUpdate(AbstractView configViewChange) {
			if (configViewChange.Id != OptionIdRobot) {
				// We only want to continue if the robot dropdown was changed
				return true;
			}
			
			// Always remove the existing job view (if present) when the robot is changed
			ConfigPage.RemoveViewById(OptionIdJob);

			SelectListView changedRobotView = (SelectListView) configViewChange;
			if (changedRobotView.Selection == -1) {
				return true;
			}
			
			HsRobot robot = _getRobot();
			if (robot == null) {
				// Shouldn't happen but whatever
				return false;
			}

			List<FavoriteJobs.FavoriteJob> favorites = robot.GetFavoriteJobs();
			if (favorites.Count == 0) {
				LabelView noJobsLabel = new LabelView(OptionIdJob, "Favorite Job", $"{robot.GetName()} has no saved favorite jobs. Configure favorite jobs from the <a href=\"/iRobot/favorites.html\">favorite jobs page</a>.");
				ConfigPage.AddView(noJobsLabel);
			} else {
				SelectListView jobList = new SelectListView(OptionIdJob, "Favorite Job", favorites.Select(j => j.Name).ToList());
				ConfigPage.AddView(jobList);
			}
			
			return true;
		}

		public override string GetPrettyString() {
			if (!IsFullyConfigured()) {
				return "";
			}

			HsRobot robot = _getRobot();
			string jobName = ConfigPage.GetViewById<SelectListView>(OptionIdJob).GetSelectedOption();
			return $"iRobot: Start favorite job <font class=\"event_Txt_Selection\">{jobName}</font> on <font class=\"event_Txt_Selection\">{robot.GetName()}</font>";
		}

		public override bool OnRunAction() {
			HsRobot robot = _getRobot();
			string jobName = ConfigPage.GetViewById<SelectListView>(OptionIdJob).GetSelectedOption();
			return robot.StartFavoriteJob(jobName);
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