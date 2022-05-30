using HomeSeer.Jui.Views;

namespace HSPI_IRobot.HsEvents.RobotActions {
	public abstract class AbstractRobotAction {
		protected readonly string PageId;
		protected readonly RobotAction Action;

		protected Page ConfigPage {
			get => Action.InternalConfigPage;
			set => Action.InternalConfigPage = value;
		}

		protected HSPI Plugin => Action.Plugin;

		protected HsRobot Robot => Action.GetRobot();

		public AbstractRobotAction(string pageId, RobotAction action) {
			PageId = pageId;
			Action = action;
		}

		// HsRobot is passed here because the Robot property might not yet be usable when this is called
		public abstract bool OnNewSubAction(HsRobot robot);
		public abstract bool IsFullyConfigured();
		public abstract bool OnConfigItemUpdate(AbstractView configViewChange);
		public abstract string GetPrettyString();
		public abstract bool OnRunAction();
	}
}