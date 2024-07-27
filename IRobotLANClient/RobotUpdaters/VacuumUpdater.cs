using IRobotLANClient.Enums;
using IRobotLANClient.JsonObjects;
using IRobotLANClient.RobotInterfaces;

namespace IRobotLANClient.RobotUpdaters;

internal class VacuumUpdater {
	private readonly IVacuumClient _vacuumClient;
	
	internal VacuumUpdater(IVacuumClient vacuumClient) {
		_vacuumClient = vacuumClient;
	}

	public void Update(ReportedStateVacuum state) {
		if (state == null) {
			return;
		}
		
		bool binPresent = state.Bin?.Present ?? false;
		bool binFull = state.Bin?.Full ?? false;
		if (!binPresent) {
			_vacuumClient.BinStatus = BinStatus.NotPresent;
		} else {
			_vacuumClient.BinStatus = binFull ? BinStatus.Full : BinStatus.Ok;
		}
			
		_vacuumClient.EvacAllowed = state.EvacAllowed ?? false;
		_vacuumClient.BinFullPause = state.BinPause;

		if (state.TwoPass) {
			_vacuumClient.CleaningPassMode = CleaningPassMode.TwoPass;
		} else if (state.NoAutoPasses) {
			_vacuumClient.CleaningPassMode = CleaningPassMode.OnePass;
		} else {
			_vacuumClient.CleaningPassMode = CleaningPassMode.AutoPass;
		}
	}
}
