using IRobotLANClient.Enums;

namespace IRobotLANClient.RobotInterfaces;

internal interface IMopClient {
	public TankStatus TankStatus { get; internal set; }
	public MopPadType MopPadType { get; internal set; }
	public byte WetMopPadWetness { get; internal set; }
	public byte? WetMopRankOverlap { get; internal set; }
	public byte TankLevel { get; internal set; }
	public byte? DockTankLevel { get; internal set; }

	public void SetWetMopPadWetness(byte wetness);
	public void SetWetMopRankOverlap(byte rankOverlap);
}