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
            	switch (option) {
            		case ConfigOption.ChargeLightRingPattern:
            			optionLabels.Add("On-dock light ring pattern");
            			break;
            		
            		case ConfigOption.ChildLock:
            			optionLabels.Add("Child lock");
            			break;
            		
            		case ConfigOption.BinFullPause:
            			optionLabels.Add("Bin full behavior");
            			break;
            		
            		case ConfigOption.CleaningPassMode:
            			optionLabels.Add("Cleaning passes");
            			break;
            		
            		case ConfigOption.WetMopPadWetness:
            			optionLabels.Add("Jet spray amount");
            			break;
            		
            		case ConfigOption.WetMopPassOverlap:
            			optionLabels.Add("Wet mopping behavior");
            			break;
            		
            		default:
            			optionLabels.Add(option.ToString());
            			break;
            	}
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
				List<string> optionKeys = new List<string>();
				List<string> optionLabels = new List<string>();
				switch (option) {
					case ConfigOption.ChargeLightRingPattern:
						optionKeys.Add(((int) ChargeLightRingPattern.DockingAndCharging).ToString());
						optionLabels.Add("Docking & charging status");
						
						optionKeys.Add(((int) ChargeLightRingPattern.Docking).ToString());
						optionLabels.Add("Docking status");
						
						optionKeys.Add(((int) ChargeLightRingPattern.None).ToString());
						optionLabels.Add("No status lights");
						break;
					
					case ConfigOption.ChildLock:
						optionKeys.Add("0");
						optionLabels.Add("Off");
						
						optionKeys.Add("1");
						optionLabels.Add("On");
						break;
					
					case ConfigOption.BinFullPause:
						optionKeys.Add("1");
						optionLabels.Add("Do not clean when full");
						
						optionKeys.Add("0");
						optionLabels.Add("Keep cleaning when full");
						break;
					
					case ConfigOption.CleaningPassMode:
						optionKeys.Add(((int) CleaningPassMode.AutoPass).ToString());
						optionLabels.Add("Room-size clean (auto depending on room size)");
						
						optionKeys.Add(((int) CleaningPassMode.OnePass).ToString());
						optionLabels.Add("Daily clean (one pass)");
						
						optionKeys.Add(((int) CleaningPassMode.TwoPass).ToString());
						optionLabels.Add("Extra clean (two passes)");
						break;
					
					case ConfigOption.WetMopPadWetness:
						optionKeys.Add("1");
						optionLabels.Add("Low");
						
						optionKeys.Add("2");
						optionLabels.Add("Medium");
						
						optionKeys.Add("3");
						optionLabels.Add("High");
						break;
					
					case ConfigOption.WetMopPassOverlap:
						optionKeys.Add("67");
						optionLabels.Add("Standard");

						optionKeys.Add("85");
						optionLabels.Add("Deep");

						optionKeys.Add("25");
						optionLabels.Add("Extended coverage");
						break;
				}

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

			switch ((ConfigOption) settingKeyInt) {
				case ConfigOption.ChargeLightRingPattern:
					Robot.Client.SetChargeLightRingPattern((ChargeLightRingPattern) settingValueInt);
					return true;
				
				case ConfigOption.ChildLock:
					Robot.Client.SetChildLock(settingValueInt != 0);
					return true;
				
				case ConfigOption.BinFullPause:
					((RobotVacuum) Robot.Client).SetBinFullPause(settingValueInt != 0);
					return true;
				
				case ConfigOption.CleaningPassMode:
					((RobotVacuum) Robot.Client).SetCleaningPassMode((CleaningPassMode) settingValueInt);
					return true;
				
				case ConfigOption.WetMopPadWetness:
					((RobotMop) Robot.Client).SetWetMopPadWetness((byte) settingValueInt);
					return true;
				
				case ConfigOption.WetMopPassOverlap:
					((RobotMop) Robot.Client).SetWetMopRankOverlap((byte) settingValueInt);
					return true;
				
				default:
					return false;
			}
		}
	}
}