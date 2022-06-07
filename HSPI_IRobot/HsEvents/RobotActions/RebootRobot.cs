using HomeSeer.Jui.Views;

namespace HSPI_IRobot.HsEvents.RobotActions {
	public class RebootRobot : AbstractRobotAction {
		public RebootRobot(string pageId, RobotAction action) : base(pageId, action) { }
		
		public override bool OnNewSubAction(HsRobot robot) {
			return true;
		}

		public override bool IsFullyConfigured() {
			return true;
		}

		public override bool OnConfigItemUpdate(AbstractView configViewChange) {
			return false;
		}

		public override string GetPrettyString() {
			return $"iRobot: Reboot <font class=\"event_Txt_Selection\">{Robot.GetName()}</font>";
		}

		public override bool OnRunAction() {
			if (!(Robot.Client?.Connected ?? false)) {
				return false;
			}
			
			Robot.Client.Reboot();
			return true;
		}
	}
}