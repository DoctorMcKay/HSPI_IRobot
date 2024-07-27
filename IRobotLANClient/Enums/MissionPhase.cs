namespace IRobotLANClient.Enums;

public enum MissionPhase {
	Unknown,
	Charge,
	Run,
	Stuck,
	Stop,
	UserSentHome,
	DockingAfterMission,
	DockingMidMission, // returning to base due to low battery, recharge & resume
	Evac,
	ChargingError
}