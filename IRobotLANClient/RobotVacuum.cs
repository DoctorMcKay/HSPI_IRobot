using IRobotLANClient.Enums;
using Newtonsoft.Json.Linq;

namespace IRobotLANClient {
	public class RobotVacuum : Robot {
		public BinStatus BinStatus { get; protected set; }

		public RobotVacuum(string address, string blid, string password) : base(address, blid, password) { }

		protected override void HandleRobotStateUpdate() {
			bool binPresent = (bool) ReportedState.SelectToken("bin.present");
			bool binFull = (bool) ReportedState.SelectToken("bin.full");
			if (!binPresent) {
				BinStatus = BinStatus.NotPresent;
			} else {
				BinStatus = binFull ? BinStatus.Full : BinStatus.Ok;
			}
		}
	}
}