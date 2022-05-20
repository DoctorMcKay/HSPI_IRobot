using System;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Devices.Identification;
using HomeSeer.PluginSdk.Logging;
using HSPI_IRobot.Enums;
using IRobotLANClient.Enums;

namespace HSPI_IRobot {
	public class FeatureCreator {
		private HSPI _plugin;
		private HsDevice _device;

		public FeatureCreator(HSPI plugin, HsDevice device) {
			_plugin = plugin;
			_device = device;
		}

		public int CreateFeature(FeatureType featureType) {
			switch (featureType) {
				case FeatureType.Status:
					return _createStatus();
				
				case FeatureType.JobPhase:
					return _createJobPhase();
				
				case FeatureType.Battery:
					return _createBattery();
				
				case FeatureType.VacuumBin:
					return _createBin();
				
				case FeatureType.MopTank:
					return _createTank();
				
				case FeatureType.MopPad:
					return _createPad();
				
				case FeatureType.Ready:
					return _createReady();
				
				case FeatureType.Error:
					return _createError();
				
				default:
					throw new Exception($"Unknown feature type {featureType}");
			}
		}

		private int _createStatus() {
			FeatureFactory factory = _getFactory()
				.WithName("Status")
				.WithAddress($"{_device.Address}:Status")
				.WithExtraData(_versionExtraData(1))
				.AddGraphicForValue("/images/HomeSeer/status/off.gif", (double) RobotStatus.OnBase, "On Home Base")
				.AddGraphicForValue("/images/HomeSeer/status/on.gif", (double) RobotStatus.Clean, "Cleaning")
				.AddGraphicForValue("/images/HomeSeer/status/pause.png", (double) RobotStatus.JobPaused, "Job Paused")
				.AddGraphicForValue("/images/HomeSeer/status/stop.png", (double) RobotStatus.OffBaseNoJob, "Off Base")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) RobotStatus.Stuck, "Stuck")
				.AddGraphicForValue("/images/HomeSeer/status/home.png", (double) RobotStatus.DockManually, "Returning To Home Base")
				.AddGraphicForValue("/images/HomeSeer/status/eject.png", (double) RobotStatus.Evac, "Emptying Bin")
				.AddGraphicForValue("/images/HomeSeer/status/zoom.png", (double) RobotStatus.Train, "Mapping Run")
				.AddButton((double) RobotStatus.Clean, "Clean")
				.AddButton((double) RobotStatus.JobPaused, "Pause")
				.AddButton((double) RobotStatus.Resume, "Resume")
				.AddButton((double) RobotStatus.OffBaseNoJob, "Abort Job")
				.AddButton((double) RobotStatus.DockManually, "Return To Home Base")
				.AddButton((double) RobotStatus.Find, "Locate");

			HSPI.HsVersion hsVersion = _plugin.GetHsVersion();
			if (hsVersion.Major >= 4 && hsVersion.Minor >= 2) {
				// This is only supported from 4.2.0.0 onward
				factory.WithDisplayType(EFeatureDisplayType.Important);
			}

			return _createFeature(factory);
		}

		private int _createJobPhase() {
			FeatureFactory factory = _getFactory()
				.WithName("Job Phase")
				.WithAddress($"{_device.Address}:JobPhase")
				.WithExtraData(_versionExtraData(1))
				.AddGraphicForValue("/images/HomeSeer/status/off.gif", (double) CleanJobPhase.NoJob, "No Job")
				.AddGraphicForValue("/images/HomeSeer/status/play.png", (double) CleanJobPhase.Cleaning, "Cleaning")
				.AddGraphicForValue("/images/HomeSeer/status/electricity.gif", (double) CleanJobPhase.Charging, "Charging")
				.AddGraphicForValue("/images/HomeSeer/status/eject.png", (double) CleanJobPhase.Evac, "Emptying Bin")
				.AddGraphicForValue("/images/HomeSeer/status/batterytoolowtooperatelock.png", (double) CleanJobPhase.LowBatteryReturningToDock, "Returning to Home Base to Recharge")
				.AddGraphicForValue("/images/HomeSeer/status/replay.png", (double) CleanJobPhase.DoneReturningToDock, "Finished, Returning To Home Base")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) CleanJobPhase.ChargingError, "Charging Error");

			return _createFeature(factory);
		}

		private int _createBattery() {
			FeatureFactory factory = _getFactory()
				.WithName("Battery")
				.WithAddress($"{_device.Address}:Battery")
				.WithExtraData(_versionExtraData(2))
				.AsType(EFeatureType.Generic, (int) EGenericFeatureSubType.Battery)
				.WithMiscFlags(EMiscFlag.StatusOnly)
				.AddGraphic(new StatusGraphic("/images/HomeSeer/status/battery_0.png", new ValueRange(0, 10) { Suffix = "%" }) { Value = 0 }) // manually specifying Value is necessary to avoid an error
				.AddGraphic(new StatusGraphic("/images/HomeSeer/status/battery_25.png", new ValueRange(11, 37) { Suffix = "%" }) { Value = 11 })
				.AddGraphic(new StatusGraphic("/images/HomeSeer/status/battery_50.png", new ValueRange(38, 63) { Suffix = "%" }) { Value = 38 })
				.AddGraphic(new StatusGraphic("/images/HomeSeer/status/battery_75.png", new ValueRange(64, 88) { Suffix = "%" }) { Value = 64 })
				.AddGraphic(new StatusGraphic("/images/HomeSeer/status/battery_100.png", new ValueRange(89, 100) { Suffix = "%" }) { Value = 89 });

			return _createFeature(factory);
		}

		private int _createBin() {
			FeatureFactory factory = _getFactory()
				.WithName("Bin")
				.WithAddress($"{_device.Address}:VacuumBin")
				.WithExtraData(_versionExtraData(1))
				.AddGraphicForValue("/images/HomeSeer/status/ok.png", (double) BinStatus.Ok, "OK")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) BinStatus.Full, "Full")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) BinStatus.NotPresent, "Removed");

			return _createFeature(factory);
		}

		private int _createTank() {
			FeatureFactory factory = _getFactory()
				.WithName("Tank")
				.WithAddress($"{_device.Address}:MopTank")
				.WithExtraData(_versionExtraData(1))
				.AddGraphicForValue("/images/HomeSeer/status/ok.png", (double) TankStatus.Ok, "OK")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) TankStatus.Empty, "Empty")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) TankStatus.LidOpen, "Lid Open");

			return _createFeature(factory);
		}

		private int _createPad() {
			FeatureFactory factory = _getFactory()
				.WithName("Pad")
				.WithAddress($"{_device.Address}:MopPad")
				.WithExtraData(_versionExtraData(1))
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) MopPadType.Invalid, "None")
				.AddGraphicForValue("/images/HomeSeer/status/water.gif", (double) MopPadType.DisposableWet, "Disposable Wet")
				.AddGraphicForValue("/images/HomeSeer/status/luminance-00.png", (double) MopPadType.DisposableDry, "Disposable Dry")
				.AddGraphicForValue("/images/HomeSeer/status/water.gif", (double) MopPadType.ReusableWet, "Reusable Wet")
				.AddGraphicForValue("/images/HomeSeer/status/luminance-00.png", (double) MopPadType.ReusableDry, "Reusable Dry");

			return _createFeature(factory);
		}

		private int _createReady() {
			FeatureFactory factory = _getFactory()
				.WithName("Readiess")
				.WithAddress($"{_device.Address}:Ready")
				.WithExtraData(_versionExtraData(3))
				.AddGraphicForValue("/images/HomeSeer/status/ok.png", 0, "Ready")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 1, "Near a cliff")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 2, "Both wheels dropped")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 3, "Left wheel dropped")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 4, "Right wheel dropped")
				.AddGraphicForRange("/images/HomeSeer/status/alarm.png", 5, 6, "Not Ready")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 7, "Insert the bin")
				.AddGraphicForRange("/images/HomeSeer/status/alarm.png", 8, 14, "Not Ready")
				.AddGraphicForValue("/images/HomeSeer/status/batterytoolowtooperatelock.png", 15, "Low battery")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 16, "Empty the bin")
				.AddGraphicForRange("/images/HomeSeer/status/alarm.png", 17, 20, "Not Ready")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 21, "Fill the tank")
				.AddGraphicForRange("/images/HomeSeer/status/alarm.png", 22, 30, "Not Ready")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 31, "Fill the tank")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 32, "Close the lid")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 33, "Not Ready")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 34, "Attach a pad")
				.AddGraphicForRange("/images/HomeSeer/status/alarm.png", 35, 38, "Not Ready")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 39, "Saving clean map")
				.AddGraphicForRange("/images/HomeSeer/status/alarm.png", 40, 67, "Not Ready")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", 68, "Saving smart map edits")
				.AddGraphicForRange("/images/HomeSeer/status/alarm.png", 69, 255, "Not Ready");

			return _createFeature(factory);
		}

		private int _createError() {
			FeatureFactory factory = _getFactory()
				.WithName("Error")
				.WithAddress($"{_device.Address}:Error")
				.WithExtraData(_versionExtraData(2))
				.AddGraphicForValue("/images/HomeSeer/status/ok.png", 0, "No Error")
				.AddGraphicForRange("/images/HomeSeer/status/alarm.png", 1, 255)
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) InternalError.DisconnectedFromRobot, "Disconnected from robot")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) InternalError.CannotDiscoverRobot, "Robot not found on network")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (double) InternalError.CannotConnectToMqtt, "Cannot connect to robot");
			
			// I considered adding an internal error code for "Rebooting" as it appears that sometimes the robot can reboot
			// itself, but such status would only last for a few seconds before flipping to DisconnectedFromRobot so I
			// ultimately decided against it. This is how an internal reboot manifests itself:
			// [AttemptConnect:66] [Upstairs i7] lastCommand.command: "start" -> "reset"
			// [AttemptConnect:66] [Upstairs i7] lastCommand.time: "1652085919" -> "1652124012"
			// [AttemptConnect:66] [Upstairs i7] lastCommand.initiator: "localApp" -> "admin"

			return _createFeature(factory);
		}

		private FeatureFactory _getFactory() {
			return FeatureFactory.CreateFeature(HSPI.PluginId, _device.Ref);
		}

		private PlugExtraData _versionExtraData(int version) {
			PlugExtraData data = new PlugExtraData();
			data.AddNamed("version", version.ToString());
			return data;
		}

		private int _createFeature(FeatureFactory factory) {
			NewFeatureData featureData = factory.PrepareForHs();
			int featureRef = _plugin.GetHsController().CreateFeatureForDevice(featureData);
			_plugin.WriteLog(ELogType.Info, $"Created new feature {featureRef} ({featureData.Feature[EProperty.Name]}) for device {_device.Ref} ({_device.Name})");
			return featureRef;
		}
	}
}
