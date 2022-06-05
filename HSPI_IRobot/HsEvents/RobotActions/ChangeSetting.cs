using System;
using System.Collections.Generic;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk.Logging;
using IRobotLANClient;
using IRobotLANClient.Enums;

namespace HSPI_IRobot.HsEvents.RobotActions {
	public class ChangeSetting : AbstractRobotAction {
		private string OptionIdRobotSetting => $"{PageId}-RobotSetting";
		private string OptionIdRobotSettingValue => $"{PageId}-RobotSettingValue";
		
		public ChangeSetting(string pageId, RobotAction action) : base(pageId, action) { }
		
		public override bool OnNewSubAction(HsRobot robot) {
			if (!(robot.Client?.Connected ?? false)) {
            	// Don't allow saving if the robot isn't connected
            	return false;
            }
            
            List<string> optionKeys = new List<string>();
            List<string> optionLabels = new List<string>();

            foreach (ConfigOption option in Enum.GetValues(typeof(ConfigOption))) {
            	if (!robot.Client.SupportsConfigOption(option)) {
            		continue;
            	}

            	optionKeys.Add(((int) option).ToString());
                optionLabels.Add(RobotOptions.GetOptionName(option));
            }

            if (optionKeys.Count == 0) {
            	LabelView label = new LabelView(OptionIdRobotSetting, "", "No configurable settings available for this robot");
            	ConfigPage.AddView(label);
            } else {
            	SelectListView setting = new SelectListView(OptionIdRobotSetting, "Setting", optionLabels, optionKeys);
            	ConfigPage.AddView(setting);
            }

            return true;
		}

		public override bool IsFullyConfigured() {
			return ConfigPage.ContainsViewWithId(OptionIdRobotSetting)
			       && ConfigPage.GetViewById(OptionIdRobotSetting) is SelectListView
			       && ConfigPage.GetViewById<SelectListView>(OptionIdRobotSetting).Selection != -1
			       && ConfigPage.ContainsViewWithId(OptionIdRobotSettingValue)
			       && ConfigPage.GetViewById<SelectListView>(OptionIdRobotSettingValue).Selection != -1;
		}

		public override bool OnConfigItemUpdate(AbstractView configViewChange) {
			if (configViewChange.Id == OptionIdRobotSetting) {
				int selection = ((SelectListView) configViewChange).Selection;
				
				ConfigPage.RemoveViewsAfterId(configViewChange.Id);
				
				if (selection == -1) {
					return true;
				}

				SelectListView settingView = ConfigPage.GetViewById<SelectListView>(OptionIdRobotSetting);
				if (
					!int.TryParse(settingView.OptionKeys[selection], out int parsedInt)
					|| !Enum.IsDefined(typeof(ConfigOption), parsedInt)
				) {
					return false;
				}

				ConfigOption option = (ConfigOption) parsedInt;
				List<string> optionKeys = RobotOptions.GetOptionKeys(option);
				List<string> optionLabels = RobotOptions.GetOptionLabels(option);

				string chosenSettingName = settingView.Options[selection];
				SelectListView optionView = new SelectListView(OptionIdRobotSettingValue, chosenSettingName, optionLabels, optionKeys);
				ConfigPage.AddView(optionView);
				return true;
			}

			if (configViewChange.Id == OptionIdRobotSettingValue) {
				// not gonna check each individual option type to make sure this is a valid choice
				return true;
			}

			return false;
		}

		public override string GetPrettyString() {
			string settingName = ConfigPage.GetViewById<SelectListView>(OptionIdRobotSetting).GetSelectedOption();
			string settingValue = ConfigPage.GetViewById<SelectListView>(OptionIdRobotSettingValue).GetSelectedOption();
			return $"iRobot: Change setting on <font class=\"event_Txt_Selection\">{Robot.GetName()}</font><br /><font class=\"event_Txt_Selection\">{settingName}</font> = <font class=\"event_Txt_Selection\">{settingValue}</font>";
		}

		public override bool OnRunAction() {
			string settingKey = ConfigPage.GetViewById<SelectListView>(OptionIdRobotSetting).GetSelectedOptionKey();
			if (!int.TryParse(settingKey, out int settingKeyInt)) {
				Plugin.WriteLog(ELogType.Debug, $"Couldn't parse \"{settingKey}\" as an int");
				return false;
			}

			if (!Enum.IsDefined(typeof(ConfigOption), settingKeyInt)) {
				Plugin.WriteLog(ELogType.Debug, $"Value {settingKeyInt} is not a ConfigOption");
				return false;
			}

			if (!(Robot.Client?.Connected ?? false)) {
				return false;
			}

			string settingValue = ConfigPage.GetViewById<SelectListView>(OptionIdRobotSettingValue).GetSelectedOptionKey();
			if (!int.TryParse(settingValue, out int settingValueInt)) {
				Plugin.WriteLog(ELogType.Debug, $"Couldn't parse \"{settingValue}\" as an int");
				return false;
			}

			return RobotOptions.ChangeSetting(Robot, (ConfigOption) settingKeyInt, settingValueInt);
		}
	}
}