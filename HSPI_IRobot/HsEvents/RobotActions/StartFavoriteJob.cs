using System.Collections.Generic;
using System.Linq;
using HomeSeer.Jui.Views;
using HSPI_IRobot.FeaturePageHandlers;

namespace HSPI_IRobot.HsEvents.RobotActions {
	public class StartFavoriteJob: AbstractRobotAction {
		private string OptionIdJobName => $"{PageId}-JobName";
		
		public StartFavoriteJob(string id, RobotAction action) : base(id, action) { }

		public override bool OnNewSubAction(HsRobot robot) {
			List<FavoriteJobs.FavoriteJob> favorites = robot.GetFavoriteJobs();
			if (favorites.Count == 0) {
				LabelView noJobsLabel = new LabelView(OptionIdJobName, "", $"{robot.GetName()} has no saved favorite jobs. Configure favorite jobs from the <a href=\"/iRobot/favorites.html\">favorite jobs page</a>.");
				ConfigPage.AddView(noJobsLabel);
			} else {
				SelectListView jobList = new SelectListView(OptionIdJobName, "Favorite Job", favorites.Select(j => j.Name).ToList());
				ConfigPage.AddView(jobList);
			}

			return true;
		}
		
		public override bool IsFullyConfigured() {
			return ConfigPage.ContainsViewWithId(OptionIdJobName)
			       && ConfigPage.GetViewById(OptionIdJobName) is SelectListView
			       && !string.IsNullOrWhiteSpace(ConfigPage.GetViewById<SelectListView>(OptionIdJobName).GetSelectedOption());
		}

		public override bool OnConfigItemUpdate(AbstractView configViewChange) {
			if (configViewChange.Id == OptionIdJobName) {
				// Make sure it's a valid favorite
				int selection = ((SelectListView) configViewChange).Selection;
				if (selection == -1) {
					return true;
				}

				string selectionName = ConfigPage.GetViewById<SelectListView>(OptionIdJobName).Options[selection];
				return Robot?.GetFavoriteJobs().Exists(job => job.Name == selectionName) ?? false;
			}

			return false;
		}

		public override string GetPrettyString() {
			if (!IsFullyConfigured()) {
				return string.Empty;
			}
			
			string jobName = ConfigPage.GetViewById<SelectListView>(OptionIdJobName).GetSelectedOption();
			bool jobExists = Robot.GetFavoriteJobs().Any(job => job.Name == jobName);
			return $"iRobot: Start favorite job <font class=\"event_Txt_Selection\">{jobName}{(!jobExists ? " (JOB NO LONGER EXISTS)" : "")}</font> on <font class=\"event_Txt_Selection\">{Robot.GetName()}</font>";
		}

		public override bool OnRunAction() {
			string jobName = ConfigPage.GetViewById<SelectListView>(OptionIdJobName).GetSelectedOption();
			return Robot?.StartFavoriteJob(jobName) ?? false;
		}
	}
}