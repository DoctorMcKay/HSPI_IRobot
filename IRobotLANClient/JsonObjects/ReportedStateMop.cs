using Newtonsoft.Json;

namespace IRobotLANClient.JsonObjects {
	[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
	public class ReportedStateMop {
		[JsonProperty("mopReady")] public MopReadyType MopReady;
		[JsonProperty("tankLvl")] public int TankLvl;
		[JsonProperty("detectedPad")] public string DetectedPad;
		[JsonProperty("padWetness")] public PadWetnessType PadWetness;
		[JsonProperty("rankOverlap")] public byte RankOverlap;

		public class MopReadyType {
			[JsonProperty("tankPresent")] public bool TankPresent;
			[JsonProperty("lidClosed")] public bool LidClosed;
		}

		public class PadWetnessType {
			[JsonProperty("disposable")] public byte Disposable;
			[JsonProperty("reusable")] public byte Reusable;
		}
	}
}