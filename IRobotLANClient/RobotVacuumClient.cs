using IRobotLANClient.Enums;
using IRobotLANClient.JsonObjects;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IRobotLANClient {
	public class RobotVacuumClient : RobotClient {
		public BinStatus BinStatus { get; protected set; }
		public bool BinFullPause { get; private set; }
		public CleaningPassMode CleaningPassMode { get; private set; }

		public RobotVacuumClient(string address, string blid, string password) : base(address, blid, password) { }

		public override bool IsCorrectRobotType() {
			return ReportedState.ContainsKey("bin");
		}

		protected override void HandleRobotStateUpdate() {
			ReportedStateVacuum state = JsonConvert.DeserializeObject<ReportedStateVacuum>(ReportedState.ToString());
			
			bool binPresent = state.Bin?.Present ?? false;
			bool binFull = state.Bin?.Full ?? false;
			if (!binPresent) {
				BinStatus = BinStatus.NotPresent;
			} else {
				BinStatus = binFull ? BinStatus.Full : BinStatus.Ok;
			}

			BinFullPause = state.BinPause;

			if (state.TwoPass) {
				CleaningPassMode = CleaningPassMode.TwoPass;
			} else if (state.NoAutoPasses) {
				CleaningPassMode = CleaningPassMode.OnePass;
			} else {
				CleaningPassMode = CleaningPassMode.AutoPass;
			}
		}

		public override bool SupportsConfigOption(ConfigOption option) {
			switch (option) {
				case ConfigOption.CleaningPassMode:
					return (int) (ReportedState.SelectToken("cap.multiPass") ?? JToken.FromObject(0)) == 2;
					
				case ConfigOption.BinFullPause:
					return (int) (ReportedState.SelectToken("cap.binFullDetect") ?? JToken.FromObject(0)) == 2;
					
				default:
					return base.SupportsConfigOption(option);
			}
		}

		public void SetBinFullPause(bool binPause) {
			UpdateOption(new {binPause});
		}

		public void SetCleaningPassMode(CleaningPassMode mode) {
			bool twoPass = false, noAutoPasses = false;
			switch (mode) {
				case CleaningPassMode.AutoPass:
					twoPass = false;
					noAutoPasses = false;
					break;
				
				case CleaningPassMode.OnePass:
					twoPass = false;
					noAutoPasses = true;
					break;
				
				case CleaningPassMode.TwoPass:
					twoPass = true;
					noAutoPasses = true;
					break;
			}
			
			UpdateOption(new {twoPass, noAutoPasses});
		}

		public void Evac() {
			SendCommand("evac");
		}
	}
}