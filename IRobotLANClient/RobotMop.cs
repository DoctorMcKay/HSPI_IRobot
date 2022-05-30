using System;
using IRobotLANClient.Enums;
using IRobotLANClient.JsonObjects;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IRobotLANClient {
	public class RobotMop : Robot {
		public TankStatus TankStatus { get; private set; }
		public MopPadType MopPadType { get; private set; }
		public byte WetMopPadWetness { get; private set; }
		public byte WetMopRankOverlap { get; private set; }
		
		public RobotMop(string address, string blid, string password) : base(address, blid, password) { }

		public override bool IsCorrectRobotType() {
			return ReportedState.ContainsKey("mopReady");
		}

		protected override void HandleRobotStateUpdate() {
			ReportedStateMop state = JsonConvert.DeserializeObject<ReportedStateMop>(ReportedState.ToString());

			bool tankPresent = state.MopReady?.TankPresent ?? false;
			bool lidClosed = state.MopReady?.LidClosed ?? false;

			// In my experience, tankPresent = false always when tankLevel = 0, so we can probably use either one to
			// determine if it needs filling

			if (!lidClosed) {
				TankStatus = TankStatus.LidOpen;
			} else {
				TankStatus = state.TankLvl > 0 && tankPresent ? TankStatus.Ok : TankStatus.Empty;
			}

			switch (state.DetectedPad) {
				case "invalid":
					MopPadType = MopPadType.Invalid;
					break;
				
				case "reusableWet":
					MopPadType = MopPadType.ReusableWet;
					break;
				
				case "reusableDry":
					MopPadType = MopPadType.ReusableDry;
					break;
				
				case "dispWet":
					MopPadType = MopPadType.DisposableWet;
					break;
				
				case "dispDry":
					MopPadType = MopPadType.DisposableDry;
					break;
				
				default:
					Console.WriteLine($"WARNING: Unknown detectedPad: {state.DetectedPad}");
					MopPadType = MopPadType.Invalid;
					break;
			}

			WetMopPadWetness = state.PadWetness?.Disposable ?? 1;
			WetMopRankOverlap = state.RankOverlap;
		}

		public override bool SupportsConfigOption(ConfigOption option) {
			switch (option) {
				case ConfigOption.WetMopPadWetness:
					return ReportedState.SelectToken("padWetness.disposable")?.Type == JTokenType.Integer;
				
				case ConfigOption.WetMopPassOverlap:
					// This key is also present on the i7 vacuum but I can't find a capability or feature flag that
					// looks like it indicates whether this can be used.
					return ReportedState.SelectToken("rankOverlap")?.Type == JTokenType.Integer;
				
				default:
					return base.SupportsConfigOption(option);
			}
		}

		public void SetWetMopPadWetness(byte wetness) {
			UpdateOption(new {
				padWetness = new {
					disposable = wetness,
					reusable = wetness
				}
			});
		}

		public void SetWetMopRankOverlap(byte rankOverlap) {
			UpdateOption(new {rankOverlap});
		}
	}
}