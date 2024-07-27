using Newtonsoft.Json;

namespace IRobotLANClient.JsonObjects;

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class ReportedStateMop {
	// mopReady ceases to exist in software version c. 22.29.3
	[JsonProperty("mopReady")] public MopReadyType MopReady;
	[JsonProperty("dock")] public DockType Dock;
	[JsonProperty("lidOpen")] public bool LidOpen;
	[JsonProperty("tankPresent")] public bool TankPresent;
	[JsonProperty("tankLvl")] public byte TankLvl;
	[JsonProperty("detectedPad")] public string DetectedPad;
	[JsonProperty("padWetness")] public PadWetnessType PadWetness;
	[JsonProperty("rankOverlap")] public byte? RankOverlap;

	public class MopReadyType {
		[JsonProperty("tankPresent")] public bool TankPresent;
		[JsonProperty("lidClosed")] public bool LidClosed;
	}

	public class PadWetnessType {
		[JsonProperty("disposable")] public byte Disposable;
		[JsonProperty("reusable")] public byte Reusable;
	}

	public class DockType {
		[JsonProperty("tankLvl")] public byte? TankLvl;
	}
}