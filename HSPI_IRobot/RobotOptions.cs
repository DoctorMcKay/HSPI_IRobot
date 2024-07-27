using System.Collections.Generic;
using System.ComponentModel;
using IRobotLANClient;
using IRobotLANClient.Enums;

namespace HSPI_IRobot;

public abstract class RobotOptions {
	public static string GetOptionName(ConfigOption option) {
		switch (option) {
			case ConfigOption.ChargeLightRingPattern:
				return "On-dock light ring pattern";
				
			case ConfigOption.ChildLock:
				return "Child lock";
				
			case ConfigOption.BinFullPause:
				return "Bin full behavior";
				
			case ConfigOption.CleaningPassMode:
				return "Cleaning passes";
				
			case ConfigOption.WetMopPadWetness:
				return "Jet spray amount";
				
			case ConfigOption.WetMopPassOverlap:
				return "Wet mopping behavior";
				
			case ConfigOption.EvacAllowed:
				return "Clean base auto-empty";
				
			default:
				throw new InvalidEnumArgumentException($"Unsupported option {option}");
		}
	}
		
	public static List<string> GetOptionKeys(ConfigOption option) {
		switch (option) {
			case ConfigOption.ChargeLightRingPattern:
				return new List<string> {
					((int) ChargeLightRingPattern.DockingAndCharging).ToString(),
					((int) ChargeLightRingPattern.Docking).ToString(),
					((int) ChargeLightRingPattern.None).ToString()
				};

			case ConfigOption.ChildLock:
				return new List<string> {
					"0",
					"1"
				};
				
			case ConfigOption.BinFullPause:
				return new List<string> {
					"1",
					"0"
				};
				
			case ConfigOption.CleaningPassMode:
				return new List<string> {
					((int) CleaningPassMode.AutoPass).ToString(),
					((int) CleaningPassMode.OnePass).ToString(),
					((int) CleaningPassMode.TwoPass).ToString()
				};
				
			case ConfigOption.WetMopPadWetness:
				return new List<string> {
					"1",
					"2",
					"3"
				};
				
			case ConfigOption.WetMopPassOverlap:
				return new List<string> {
					"67",
					"85",
					"25"
				};
				
			case ConfigOption.EvacAllowed:
				return new List<string> {
					"1",
					"0"
				};
				
			default:
				throw new InvalidEnumArgumentException($"Unsupported option {option}");
		}
	}

	public static List<string> GetOptionLabels(ConfigOption option) {
		switch (option) {
			case ConfigOption.ChargeLightRingPattern:
				return new List<string> {
					"Docking & charging status",
					"Docking status",
					"No status lights"
				};

			case ConfigOption.ChildLock:
				return new List<string> {
					"Off",
					"On"
				};
				
			case ConfigOption.BinFullPause:
				return new List<string> {
					"Do not clean when full",
					"Keep cleaning when full"
				};
				
			case ConfigOption.CleaningPassMode:
				return new List<string> {
					"Room-size clean (auto depending on room size)",
					"Daily clean (one pass)",
					"Extra clean (two passes)"
				};
				
			case ConfigOption.WetMopPadWetness:
				return new List<string> {
					"Low",
					"Medium",
					"High"
				};
				
			case ConfigOption.WetMopPassOverlap:
				return new List<string> {
					"Standard",
					"Deep",
					"Extended coverage"
				};
				
			case ConfigOption.EvacAllowed:
				return new List<string> {
					"Auto-empty enabled",
					"Auto-empty disabled"
				};
				
			default:
				throw new InvalidEnumArgumentException($"Unsupported option {option}");
		}
	}

	public static bool ChangeSetting(HsRobot robot, ConfigOption option, int settingValue) {
		switch (option) {
			case ConfigOption.ChargeLightRingPattern:
				robot.Client.SetChargeLightRingPattern((ChargeLightRingPattern) settingValue);
				return true;
				
			case ConfigOption.ChildLock:
				robot.Client.SetChildLock(settingValue != 0);
				return true;
				
			case ConfigOption.BinFullPause:
				((RobotVacuumClient) robot.Client).SetBinFullPause(settingValue != 0);
				return true;
				
			case ConfigOption.CleaningPassMode:
				((RobotVacuumClient) robot.Client).SetCleaningPassMode((CleaningPassMode) settingValue);
				return true;
				
			case ConfigOption.WetMopPadWetness:
				((RobotMopClient) robot.Client).SetWetMopPadWetness((byte) settingValue);
				return true;
				
			case ConfigOption.WetMopPassOverlap:
				((RobotMopClient) robot.Client).SetWetMopRankOverlap((byte) settingValue);
				return true;
				
			case ConfigOption.EvacAllowed:
				((RobotVacuumClient) robot.Client).SetEvacAllowed(settingValue != 0);
				return true;
				
			default:
				return false;
		}
	}
}