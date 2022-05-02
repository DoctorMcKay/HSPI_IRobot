using System;
using IRobotLANClient.Enums;
using Newtonsoft.Json.Linq;

namespace IRobotLANClient {
	public class RobotMop : Robot {
		public TankStatus TankStatus { get; private set; }
		public MopPadType MopPadType { get; private set; }
		
		public RobotMop(string address, string blid, string password) : base(address, blid, password) { }

		protected override void HandleRobotStateUpdate() {
			bool tankPresent = (bool) ReportedState.SelectToken("mopReady.tankPresent");
			bool lidClosed = (bool) ReportedState.SelectToken("mopReady.lidClosed");
			int tankLevel = (int) ReportedState.SelectToken("tankLvl");
			string detectedPad = (string) ReportedState.SelectToken("detectedPad");

			// In my experience, tankPresent = false always when tankLevel = 0, so we can probably use either one to determine if it needs filling

			if (!lidClosed) {
				TankStatus = TankStatus.LidOpen;
			} else {
				TankStatus = tankLevel > 0 && tankPresent ? TankStatus.Ok : TankStatus.Empty;
			}

			switch (detectedPad) {
				case "invalid":
					MopPadType = MopPadType.Invalid;
					break;
				
				case "reusableWet":
					MopPadType = MopPadType.ReusableWet;
					break;
				
				case "reusableDry":
					MopPadType = MopPadType.ReusableDry;
					break;
				
				case "disposableWet": // this value is a guess
					MopPadType = MopPadType.DisposableWet;
					break;
				
				case "disposableDry": // this value is a guess
					MopPadType = MopPadType.DisposableDry;
					break;
				
				default:
					Console.WriteLine($"WARNING: Unknown detectedPad: {detectedPad}");
					MopPadType = MopPadType.Invalid;
					break;
			}
		}
	}
}