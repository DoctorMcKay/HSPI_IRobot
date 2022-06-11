using System;
using System.Collections.Generic;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Devices.Identification;
using HomeSeer.PluginSdk.Logging;
using HSPI_IRobot.Enums;

namespace HSPI_IRobot {
	public class FeatureUpdater {
		private readonly HSPI _plugin;

		public FeatureUpdater() {
			_plugin = HSPI.Instance;
		}
		
		public void ExecuteFeatureUpdates(HsFeature feature) {
			string[] addressParts = feature.Address.Split(':');

			Func<HsFeature, int, int> updateMethod = null;

			switch (addressParts[1]) {
				case "Battery":
					updateMethod = ExecuteBatteryUpdate;
					break;
					
				case "Ready":
					updateMethod = ExecuteReadyUpdate;
					break;
				
				case "Error":
					updateMethod = ExecuteErrorUpdate;
					break;
			}

			if (updateMethod == null) {
				return;
			}

			try {
				// This feature has updates that exist, so let's run them. Keep running the update until all updates have
				// been run (indicated by false return).
				int featureVersion, newFeatureVersion;
				do {
					// Update the feature
					feature = _plugin.GetHsController().GetFeatureByRef(feature.Ref);
					featureVersion = int.Parse(feature.PlugExtraData["version"]);
					newFeatureVersion = updateMethod(feature, featureVersion);
				} while (newFeatureVersion > featureVersion); // keep updating as long as it changes things
				
				_plugin.BackupPlugExtraData(feature.Ref);
			} catch (KeyNotFoundException) {
				_plugin.WriteLog(ELogType.Error, $"Feature {feature.Ref} is corrupt. Attempting automatic repair.");
				if (_plugin.RestorePlugExtraData(feature.Ref)) {
					_plugin.WriteLog(ELogType.Info, $"Repair of feature {feature.Ref} succeeded");
					ExecuteFeatureUpdates(feature);
					return;
				}

				_plugin.WriteLog(ELogType.Error, $"Feature {feature.Ref} is irreparably corrupt. Deleting it so it can be re-created.");
				_plugin.GetHsController().DeleteFeature(feature.Ref);
				_plugin.WriteLog(ELogType.Error, "Restarting plugin");
				Environment.Exit(1);
			}
		}

		private int ExecuteBatteryUpdate(HsFeature feature, int featureVersion) {
			switch (featureVersion) {
				case 1:
					_plugin.WriteLog(ELogType.Info, $"Updating feature {feature.Ref} (Battery) to version 2");

					HSPI.HsVersion hsVersion = _plugin.GetHsVersion();
					if (hsVersion.Major >= 4 && hsVersion.Minor >= 2) {
						feature.DisplayType = EFeatureDisplayType.Normal;
					}

					feature.TypeInfo = new TypeInfo {ApiType = EApiType.Feature, Type = (int) EDeviceType.Generic, SubType = (int) EGenericFeatureSubType.Battery};
					feature.AddMiscFlag(EMiscFlag.StatusOnly);
					_plugin.GetHsController().UpdateFeatureByRef(feature.Ref, feature.Changes);

					// Add % suffix to value ranges
					_plugin.GetHsController().ClearStatusGraphicsByRef(feature.Ref);
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic("/images/HomeSeer/status/battery_0.png", new ValueRange(0, 10) {Suffix = "%"}));
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic("/images/HomeSeer/status/battery_25.png", new ValueRange(11, 37) {Suffix = "%"}));
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic("/images/HomeSeer/status/battery_50.png", new ValueRange(38, 63) {Suffix = "%"}));
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic("/images/HomeSeer/status/battery_75.png", new ValueRange(64, 88) {Suffix = "%"}));
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic("/images/HomeSeer/status/battery_100.png", new ValueRange(89, 100) {Suffix = "%"}));

					UpdateFeatureVersionNumber(feature.Ref, 2);
					return 2;
			}
			
			// No applicable update
			return featureVersion;
		}

		private int ExecuteReadyUpdate(HsFeature feature, int featureVersion) {
			switch (featureVersion) {
				case 1:
					_plugin.WriteLog(ELogType.Info, $"Updating feature {feature.Ref} (Ready) to version 2");
					_plugin.GetHsController().DeleteStatusGraphicByValue(feature.Ref, 40);
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic(
						"/images/HomeSeer/status/alarm.png",
						40,
						67
					) { Label = "Not Ready" });
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic(
						"/images/HomeSeer/status/alarm.png",
						68,
						"Saving smart map edits"
					));
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic(
						"/images/HomeSeer/status/alarm.png",
						69,
						255
					) { Label = "Not Ready" });

					UpdateFeatureVersionNumber(feature.Ref, 2);
					return 2;
				
				case 2:
					_plugin.WriteLog(ELogType.Info, $"Updating feature {feature.Ref} (Ready) to version 3");
					_plugin.GetHsController().DeleteStatusGraphicByValue(feature.Ref, 17);
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic(
						"/images/HomeSeer/status/alarm.png",
						17,
						20
					) { Label = "Not Ready"});
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic(
						"/images/HomeSeer/status/alarm.png",
						21,
						"Fill the tank"
					));
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic(
						"/images/HomeSeer/status/alarm.png",
						22,
						30
					) { Label = "Not Ready"});

					UpdateFeatureVersionNumber(feature.Ref, 3);
					return 3;
			}

			// No applicable update
			return featureVersion;
		}

		private int ExecuteErrorUpdate(HsFeature feature, int featureVersion) {
			switch (featureVersion) {
				case 1:
					_plugin.WriteLog(ELogType.Info, $"Updating feature {feature.Ref} (Error) to version 2");
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic(
						"/images/HomeSeer/status/alarm.png",
						(double) InternalError.DisconnectedFromRobot,
						"Disconnected from robot"
					));
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic(
						"/images/HomeSeer/status/alarm.png",
						(double) InternalError.CannotDiscoverRobot,
						"Robot not found on network"
					));
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic(
						"/images/HomeSeer/status/alarm.png",
						(double) InternalError.CannotConnectToMqtt,
						"Cannot connect to robot"
					));

					UpdateFeatureVersionNumber(feature.Ref, 2);
					return 2;
				
				case 2:
					_plugin.WriteLog(ELogType.Info, $"Updating feature {feature.Ref} (Error) to version 3");
					_plugin.GetHsController().AddStatusGraphicToFeature(feature.Ref, new StatusGraphic(
						"/images/HomeSeer/status/mute.png",
						(double) InternalError.ConnectionDisabled,
						"Connection disabled by user or event"
					));

					UpdateFeatureVersionNumber(feature.Ref, 3);
					return 3;
			}

			// No applicable update
			return featureVersion;
		}

		private void UpdateFeatureVersionNumber(int featureRef, int version) {
			PlugExtraData extraData = (PlugExtraData) _plugin.GetHsController().GetPropertyByRef(featureRef, EProperty.PlugExtraData);
			
			if (!extraData.ContainsNamed("version")) {
				extraData.AddNamed("version", version.ToString());
			} else {
				extraData["version"] = version.ToString();
			}

			_plugin.GetHsController().UpdatePropertyByRef(featureRef, EProperty.PlugExtraData, extraData);
		}
	}
}