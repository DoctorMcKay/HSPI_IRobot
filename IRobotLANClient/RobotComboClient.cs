using IRobotLANClient.Enums;
using IRobotLANClient.JsonObjects;
using IRobotLANClient.RobotInterfaces;
using IRobotLANClient.RobotUpdaters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IRobotLANClient;

public class RobotComboClient : RobotClient, IVacuumClient, IMopClient {
	// Vacuum properties
	public BinStatus BinStatus { get; set; }
	public bool EvacAllowed { get; set; }
	public bool BinFullPause { get; set; }
	public CleaningPassMode CleaningPassMode { get; set; }
		
	// Mop properties
	public TankStatus TankStatus { get; set; }
	public MopPadType MopPadType { get; set; }
	public byte WetMopPadWetness { get; set; }
	public byte? WetMopRankOverlap { get; set; }
	public byte TankLevel { get; set; }
	public byte? DockTankLevel { get; set; }
		
	private readonly VacuumUpdater _vacuumUpdater;
	private readonly MopUpdater _mopUpdater;
		
	public RobotComboClient(string address, string blid, string password) : base(address, blid, password) {
		_vacuumUpdater = new VacuumUpdater(this);
		_mopUpdater = new MopUpdater(this);
	}

	public override bool IsCorrectRobotType() {
		return ReportedState.ContainsKey("bin") && ReportedState.ContainsKey("padWetness");
	}

	protected override void HandleRobotStateUpdate() {
		ReportedStateVacuum vacuumState = JsonConvert.DeserializeObject<ReportedStateVacuum>(ReportedState.ToString());
		ReportedStateMop mopState = JsonConvert.DeserializeObject<ReportedStateMop>(ReportedState.ToString());
			
		_vacuumUpdater.Update(vacuumState);
		_mopUpdater.Update(mopState);
	}
	
	public override bool SupportsConfigOption(ConfigOption option) {
		switch (option) {
			case ConfigOption.CleaningPassMode:
				return (int) (ReportedState.SelectToken("cap.multiPass") ?? JToken.FromObject(0)) == 2;
					
			case ConfigOption.BinFullPause:
				return (int) (ReportedState.SelectToken("cap.binFullDetect") ?? JToken.FromObject(0)) == 2;
				
			case ConfigOption.EvacAllowed:
				return ReportedState.SelectToken("evacAllowed") != null;
			
			case ConfigOption.WetMopPadWetness:
				JToken wetMopPadWetnessToken =
					ReportedState.SelectToken("padWetness.disposable") ?? ReportedState.SelectToken("padWetness.reusable");
				return wetMopPadWetnessToken?.Type == JTokenType.Integer;
				
			case ConfigOption.WetMopPassOverlap:
				// This key is also present on the i7 vacuum but I can't find a capability or feature flag that
				// looks like it indicates whether this can be used.
				return ReportedState.SelectToken("rankOverlap")?.Type == JTokenType.Integer;
					
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

	public void SetEvacAllowed(bool evacAllowed) {
		UpdateOption(new {evacAllowed});
	}

	public void Evac() {
		SendCommand("evac");
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