using IRobotLANClient.Enums;
using IRobotLANClient.JsonObjects;
using IRobotLANClient.RobotInterfaces;
using IRobotLANClient.RobotUpdaters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IRobotLANClient;

public class RobotMopClient : RobotClient, IMopClient {
	public TankStatus TankStatus { get; set; }
	public MopPadType MopPadType { get; set; }
	public byte WetMopPadWetness { get; set; }
	public byte? WetMopRankOverlap { get; set; }
	public byte TankLevel { get; set; }
	public byte? DockTankLevel { get; set; }

	private readonly MopUpdater _mopUpdater;

	public RobotMopClient(string address, string blid, string password) : base(address, blid, password) {
		_mopUpdater = new MopUpdater(this);
	}

	public override bool IsCorrectRobotType() {
		return ReportedState.ContainsKey("padWetness");
	}

	protected override void HandleRobotStateUpdate() {
		ReportedStateMop state = JsonConvert.DeserializeObject<ReportedStateMop>(ReportedState.ToString());
		_mopUpdater.Update(state);
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