using System.Threading.Tasks;
using IRobotLANClient.Enums;
using Newtonsoft.Json.Linq;

namespace IRobotLANClient {
	public class RobotVacuum : Robot {
		public BinStatus BinStatus { get; protected set; }

		public RobotVacuum(string address, string blid, string password) : base(address, blid, password) { }

		public override bool IsCorrectRobotType() {
			return ReportedState.ContainsKey("bin");
		}

		protected override void HandleRobotStateUpdate() {
			bool binPresent = (bool) (ReportedState.SelectToken("bin.present") ?? false);
			bool binFull = (bool) (ReportedState.SelectToken("bin.full") ?? false);
			if (!binPresent) {
				BinStatus = BinStatus.NotPresent;
			} else {
				BinStatus = binFull ? BinStatus.Full : BinStatus.Ok;
			}
		}

		public void Evac() {
			SendCommand("evac");
		}
	}
}