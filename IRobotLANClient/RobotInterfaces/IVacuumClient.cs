using IRobotLANClient.Enums;

namespace IRobotLANClient.RobotInterfaces;

public interface IVacuumClient {
	public BinStatus BinStatus { get; internal set; }
	public bool EvacAllowed { get; internal set; }
	public bool BinFullPause { get; internal set; }
	public CleaningPassMode CleaningPassMode { get; internal set; }

	public void SetBinFullPause(bool binPause);
	public void SetCleaningPassMode(CleaningPassMode mode);
	public void SetEvacAllowed(bool evacAllowed);
	public void Evac();
}