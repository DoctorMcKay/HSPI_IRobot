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
			int featureVersion = int.Parse(feature.PlugExtraData["version"]);

			switch (addressParts[1]) {
				case "Error":
					ExecuteErrorUpdate(feature, featureVersion);
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

					PlugExtraData extraData = feature.PlugExtraData;
					extraData["version"] = "2";
					_plugin.GetHsController().UpdatePropertyByRef(feature.Ref, EProperty.PlugExtraData, extraData);
					
					break;
			}
		}
	}
}