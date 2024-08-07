﻿using IRobotLANClient.Enums;
using IRobotLANClient.JsonObjects;
using IRobotLANClient.RobotInterfaces;
using IRobotLANClient.RobotUpdaters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IRobotLANClient;

public class RobotVacuumClient : RobotClient, IVacuumClient {
	public BinStatus BinStatus { get; set; }
	public bool EvacAllowed { get; set; }
	public bool BinFullPause { get; set; }
	public CleaningPassMode CleaningPassMode { get; set; }

	private readonly VacuumUpdater _vacuumUpdater;

	public RobotVacuumClient(string address, string blid, string password) : base(address, blid, password) {
		_vacuumUpdater = new VacuumUpdater(this);
	}

	public override bool IsCorrectRobotType() {
		return ReportedState.ContainsKey("bin");
	}

	protected override void HandleRobotStateUpdate() {
		ReportedStateVacuum state = JsonConvert.DeserializeObject<ReportedStateVacuum>(ReportedState.ToString());
		_vacuumUpdater.Update(state);
	}

	public override bool SupportsConfigOption(ConfigOption option) {
		switch (option) {
			case ConfigOption.CleaningPassMode:
				return (int) (ReportedState.SelectToken("cap.multiPass") ?? JToken.FromObject(0)) == 2;
					
			case ConfigOption.BinFullPause:
				return (int) (ReportedState.SelectToken("cap.binFullDetect") ?? JToken.FromObject(0)) == 2;
				
			case ConfigOption.EvacAllowed:
				return ReportedState.SelectToken("evacAllowed") != null;
					
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
}