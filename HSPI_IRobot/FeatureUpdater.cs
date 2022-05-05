using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Logging;
using HSPI_IRobot.Enums;

namespace HSPI_IRobot {
	public class FeatureUpdater {
		private HSPI _plugin;

		public FeatureUpdater(HSPI plugin) {
			_plugin = plugin;
		}
		
		public void ExecuteFeatureUpdates(HsFeature feature) {
			string[] addressParts = feature.Address.Split(':');
			int featureVersion = int.Parse(feature.PlugExtraData.ContainsNamed("version") ? feature.PlugExtraData["version"] : "1");

			switch (addressParts[1]) {
				case "Ready":
					ExecuteReadyUpdate(feature, featureVersion);
					break;
				
				case "Error":
					ExecuteErrorUpdate(feature, featureVersion);
					break;
			}
		}

		private void ExecuteReadyUpdate(HsFeature feature, int featureVersion) {
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
					break;
			}
		}

		private void ExecuteErrorUpdate(HsFeature feature, int featureVersion) {
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
					break;
			}
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