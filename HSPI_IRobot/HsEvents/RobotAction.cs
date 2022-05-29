using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HomeSeer.Jui.Types;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Events;
using HSPI_IRobot.FeaturePageHandlers;

namespace HSPI_IRobot.HsEvents {
	public class RobotAction : AbstractActionType {
		public const int ActionNumber = 1;

		private string OptionIdSubAction => $"{PageId}-SubAction";
		private string OptionIdRobot => $"{PageId}-Robot";
		private string OptionIdJobName => $"{PageId}-JobName";

		private SubAction? ConfiguredSubAction {
			get {
				if (ConfigPage == null || !ConfigPage.ContainsViewWithId(OptionIdSubAction)) {
					return null;
				}

				SelectListView subActionView = ConfigPage.GetViewById<SelectListView>(OptionIdSubAction);
				if (subActionView.Selection == -1) {
					return null;
				}

				return (SubAction) subActionView.Selection;
			}
		}
		
		private HSPI Plugin => (HSPI) ActionListener;

		public RobotAction() { }

		public RobotAction(int id, int eventRef, byte[] dataIn, ActionTypeCollection.IActionTypeListener listener, bool logDebug = false) : base(id, eventRef, dataIn, listener, logDebug) {
			if (ConfigPage.ViewCount <= 0 || ConfigPage.ContainsViewWithId(OptionIdSubAction)) {
				return;
			}

			// Need to migrate to add sub-action view
			Dictionary<string, string> viewSelections = new Dictionary<string, string>();
			foreach (SelectListView view in ConfigPage.Views.Cast<SelectListView>()) {
				List<string> options = view.OptionKeys ?? view.Options;
				viewSelections.Add(view.Id, view.Selection == -1 ? string.Empty : options[view.Selection]);
			}

			// Re-init the config page
			OnNewAction();

			SelectListView slv = ConfigPage.GetViewById<SelectListView>(OptionIdSubAction);
			slv.Selection = (int) SubAction.StartFavoriteJob;
			OnConfigItemUpdate(slv);
			ConfigPage.UpdateViewById(slv);

			// For each of these subsequent steps, we need to check to make sure the view we're looking for exists.
			// It's possible that the value that was previously selected for this event is no longer valid.

			if (!ConfigPage.ContainsViewWithId(OptionIdRobot)) {
				return;
			}

			slv = ConfigPage.GetViewById<SelectListView>(OptionIdRobot);
			slv.Selection = slv.OptionKeys.IndexOf(viewSelections[OptionIdRobot]);
			OnConfigItemUpdate(slv);
			ConfigPage.UpdateViewById(slv);

			if (!ConfigPage.ContainsViewWithId(OptionIdJobName)) {
				return;
			}
			
			slv = ConfigPage.GetViewById<SelectListView>(OptionIdJobName);
			slv.Selection = slv.Options.IndexOf(viewSelections[OptionIdJobName]);
			OnConfigItemUpdate(slv);
			ConfigPage.UpdateViewById(slv);
		}

		protected override string GetName() => "iRobot Actions";

		protected sealed override void OnNewAction() {
			List<string> subActionNames = new List<string> {
				"Start Favorite Job",
				"Reboot Robot"
			};

            PageFactory factory = PageFactory.CreateEventActionPage(PageId, Name);
            factory.WithDropDownSelectList(OptionIdSubAction, "iRobot Action", subActionNames);
            ConfigPage = factory.Page;
		}

		public override bool IsFullyConfigured() {
			if (
				ConfiguredSubAction == null
				|| !ConfigPage.ContainsViewWithId(OptionIdRobot)
				|| ConfigPage.GetViewById<SelectListView>(OptionIdRobot).Selection == -1
			) {
				return false;
			}

			switch (ConfiguredSubAction) {
				case SubAction.StartFavoriteJob:
					return ConfigPage.ContainsViewWithId(OptionIdJobName)
					       && ConfigPage.GetViewById(OptionIdJobName) is SelectListView
					       && !string.IsNullOrWhiteSpace(ConfigPage.GetViewById<SelectListView>(OptionIdJobName).GetSelectedOption());
				
				case SubAction.RebootRobot:
					return true;
				
				default:
					return false;
			}
		}

		protected sealed override bool OnConfigItemUpdate(AbstractView configViewChange) {
			if (configViewChange.Id == OptionIdSubAction) {
				int selection = ((SelectListView) configViewChange).Selection;
				OnNewAction(); // re-init the page to clear any other views
				
				if (selection > -1) {
					// All sub-actions require a robot selection, so go ahead and add that
					_addRobotSelectionView();
				}

				return true;
			}

			if (configViewChange.Id == OptionIdRobot) {
				int selection = ((SelectListView) configViewChange).Selection;
				
				// Whenever this changes, we want to nuke all views except SubAction and Robot
				ConfigPage.RemoveViewsAfterId(OptionIdRobot);

				if (selection == -1) {
					// Selection cleared
					return true;
				}
				
				HsRobot robot = _getRobot(ConfigPage.GetViewById<SelectListView>(OptionIdRobot).OptionKeys[selection]);
				if (robot == null) {
					// Shouldn't happen but whatever
					return false;
				}

				switch (ConfiguredSubAction) {
					case SubAction.StartFavoriteJob:
						List<FavoriteJobs.FavoriteJob> favorites = robot.GetFavoriteJobs();
						if (favorites.Count == 0) {
							LabelView noJobsLabel = new LabelView(OptionIdJobName, "Favorite Job", $"{robot.GetName()} has no saved favorite jobs. Configure favorite jobs from the <a href=\"/iRobot/favorites.html\">favorite jobs page</a>.");
							ConfigPage.AddView(noJobsLabel);
						} else {
							SelectListView jobList = new SelectListView(OptionIdJobName, "Favorite Job", favorites.Select(j => j.Name).ToList());
							ConfigPage.AddView(jobList);
						}

						return true;
					
					case SubAction.RebootRobot:
						return true;
					
					default:
						return false;
				}
			}

			if (ConfiguredSubAction == SubAction.StartFavoriteJob && configViewChange.Id == OptionIdJobName) {
				// Make sure it's a valid favorite
				int selection = ((SelectListView) configViewChange).Selection;
				if (selection == -1) {
					return true;
				}

				string selectionName = ConfigPage.GetViewById<SelectListView>(OptionIdJobName).Options[selection];
				
				HsRobot robot = _getRobot();
				return robot != null && robot.GetFavoriteJobs().Exists(job => job.Name == selectionName);
			}

			return false;
		}

		private void _addRobotSelectionView() {
			int selection = -1;
			if (ConfigPage.ContainsViewWithId(OptionIdRobot)) {
				selection = ConfigPage.GetViewById<SelectListView>(OptionIdRobot).Selection;
				ConfigPage.RemoveViewById(OptionIdRobot);
			}
			
			List<KeyValuePair<string, string>> robots = Plugin.HsRobots.Select(robot => new KeyValuePair<string, string>(robot.Blid, robot.GetName())).ToList();
			robots.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.Ordinal));
			List<string> robotIds = robots.Select(robot => robot.Key).ToList();
			List<string> robotNames = robots.Select(robot => robot.Value).ToList();

			ConfigPage.AddView(new SelectListView(OptionIdRobot, "Robot", robotNames, robotIds, ESelectListType.DropDown, selection));
		}

		public override string GetPrettyString() {
			if (!IsFullyConfigured()) {
				return string.Empty;
			}

			HsRobot robot = _getRobot();

			switch (ConfiguredSubAction) {
				case SubAction.StartFavoriteJob:
					string jobName = ConfigPage.GetViewById<SelectListView>(OptionIdJobName).GetSelectedOption();
					bool jobExists = robot.GetFavoriteJobs().Any(job => job.Name == jobName);
					return $"iRobot: Start favorite job <font class=\"event_Txt_Selection\">{jobName}{(!jobExists ? " (JOB NO LONGER EXISTS)" : "")}</font> on <font class=\"event_Txt_Selection\">{robot.GetName()}</font>";
				
				case SubAction.RebootRobot:
					return $"iRobot: Reboot <font class=\"event_Txt_Selection\">{robot.GetName()}</font>";
				
				default:
					return string.Empty;
			}
		}

		public override bool OnRunAction() {
			HsRobot robot = _getRobot();

			switch (ConfiguredSubAction) {
				case SubAction.StartFavoriteJob:
					string jobName = ConfigPage.GetViewById<SelectListView>(OptionIdJobName).GetSelectedOption();
					return robot.StartFavoriteJob(jobName);
				
				case SubAction.RebootRobot:
					if (robot.Robot == null || !robot.Robot.Connected) {
						return false;
					}
					
					robot.Robot.Reboot();
					return true;
				
				default:
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

		public bool ReferencesFavoriteJob(string blid, string jobName) {
			return ConfiguredSubAction == SubAction.StartFavoriteJob
				   && IsFullyConfigured()
			       && _getRobotBlid() == blid
			       && ConfigPage.GetViewById<SelectListView>(OptionIdJobName).GetSelectedOption() == jobName;
		}

		public string GetEventGroupAndName() {
			EventData evt = HSPI.Instance.GetHsController().GetEventByRef(EventRef);
			return $"{evt.GroupName} {evt.Event_Name}";
		}
		
		private string _getRobotBlid() {
			if (ConfigPage == null || !ConfigPage.ContainsViewWithId(OptionIdRobot)) {
				return null;
			}

			string blid = ConfigPage.GetViewById<SelectListView>(OptionIdRobot).GetSelectedOptionKey();
			return string.IsNullOrEmpty(blid) ? null : blid;
		}

		private HsRobot _getRobot(string blid = null) {
			blid = blid ?? _getRobotBlid();
			if (blid == null) {
				return null;
			}

			return Plugin.HsRobots.Find(r => r.Blid == blid);
		}

		private enum SubAction : int {
			StartFavoriteJob = 0,
			RebootRobot
		}
	}
}