namespace HSPI_IRobot.Enums {
	public enum CleanJobPhase : int {
		NoJob = 0,
		Cleaning = 1,
		Charging = 2,
		Evac = 3,
		LowBatteryReturningToDock = 4,
		DoneReturningToDock = 5,
		ChargingError = 6
	}
}
