namespace HSPI_IRobot.Enums;

public enum RobotStatus : int {
	OnBase = 0,
	Clean = 1,
	JobPaused = 2,
	Resume = 3,
	OffBaseNoJob = 4,
	Stuck = 5,
	DockManually = 6,
	Find = 7,
	Evac = 8,
	Train = 9
}