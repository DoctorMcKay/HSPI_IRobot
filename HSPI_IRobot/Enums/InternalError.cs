namespace HSPI_IRobot.Enums;

public enum InternalError : int {
	None = 0,
	DisconnectedFromRobot = 10001,
	CannotDiscoverRobot = 10002,
	CannotConnectToMqtt = 10003,
	ConnectionDisabled = 10004
}