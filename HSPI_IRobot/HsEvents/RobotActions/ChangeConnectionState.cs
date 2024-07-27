using System.Collections.Generic;
using HomeSeer.Jui.Types;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk.Events;

namespace HSPI_IRobot.HsEvents.RobotActions;

public class ChangeConnectionState : AbstractRobotAction {
	private string OptionIdConnectionState => $"{PageId}-ConnectionState";
		
	public ChangeConnectionState(string pageId, RobotAction action) : base(pageId, action) { }
		
	public override bool OnNewSubAction(HsRobot robot) {
		List<string> optionLabels = new List<string> {
			"Disconnect from robot and don't attempt to re-connect automatically",
			"Re-connect to robot"
		};
			
		List<string> optionKeys = new List<string> {"0", "1"};

		SelectListView connectionState = new SelectListView(OptionIdConnectionState, "Connection State", optionLabels, optionKeys);
		ConfigPage.AddView(connectionState);
		return true;
	}

	public override bool IsFullyConfigured() {
		return ConfigPage.ContainsViewWithId(OptionIdConnectionState)
		       && ConfigPage.GetViewById(OptionIdConnectionState) is SelectListView
		       && ConfigPage.GetViewById<SelectListView>(OptionIdConnectionState).Selection != -1;
	}

	public override bool OnConfigItemUpdate(AbstractView configViewChange) {
		return true;
	}

	public override string GetPrettyString() {
		bool shouldConnect = ConfigPage.GetViewById<SelectListView>(OptionIdConnectionState).GetSelectedOptionKey() == "1";
		string valueString = shouldConnect ? "Re-connect to" : "Disconnect from";
		return $"iRobot: {valueString} robot <font class=\"event_Txt_Selection\">{Robot.GetName()}</font>";
	}

	public override bool OnRunAction() {
		EventData eventData = Plugin.GetHsController().GetEventByRef(Action.EventRef);

		bool shouldConnect = ConfigPage.GetViewById<SelectListView>(OptionIdConnectionState).GetSelectedOptionKey() == "1";
		return shouldConnect ? Robot.EnableConnection() : Robot.DisableConnection($"event {eventData.GroupName} {eventData.Event_Name}");
	}
}