using Newtonsoft.Json;

namespace IRobotLANClient.JsonObjects {
	[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
	public class ReportedStateVacuum {
		[JsonProperty("bin")] public BinType Bin;
		[JsonProperty("binPause")] public bool BinPause;
		[JsonProperty("noAutoPasses")] public bool NoAutoPasses;
		[JsonProperty("twoPass")] public bool TwoPass;

		public class BinType {
			[JsonProperty("present")] public bool Present;
			[JsonProperty("full")] public bool Full;
		}
	}
}