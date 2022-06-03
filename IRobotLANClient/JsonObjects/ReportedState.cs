using Newtonsoft.Json;

namespace IRobotLANClient.JsonObjects {
	[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
	public class ReportedState {
		[JsonProperty("name")] public string Name;
		[JsonProperty("sku")] public string Sku;
		[JsonProperty("batPct")] public byte BatPct;
		[JsonProperty("childLock")] public bool ChildLock;
		[JsonProperty("chrgLrPtrn")] public byte ChrgLrPtrn;
		[JsonProperty("cleanMissionStatus")] public CleanMissionStatusType CleanMissionStatus;
		[JsonProperty("pmapLearningAllowed")] public bool PmapLearningAllowed;
		[JsonProperty("otaDownloadProgress")] public byte OtaDownloadProgress;
		[JsonProperty("softwareVer")] public string SoftwareVer;

		public class CleanMissionStatusType {
			[JsonProperty("cycle")] public string Cycle;
			[JsonProperty("phase")] public string Phase;
			[JsonProperty("error")] public int Error;
			[JsonProperty("notReady")] public int NotReady;
		}
	}
}
