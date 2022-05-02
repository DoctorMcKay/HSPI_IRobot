using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Disconnecting;
using MQTTnet.Client.Receiving;

namespace IRobotLANClient {
	internal class MqttHandler : IMqttClientConnectedHandler, IMqttClientDisconnectedHandler, IMqttApplicationMessageReceivedHandler {
		private readonly Robot Robot;
		
		internal MqttHandler(Robot robot) {
			Robot = robot;
		}
		
		public Task HandleConnectedAsync(MqttClientConnectedEventArgs eventArgs) {
			Robot.ConnectedStateChanged(true);
			return Task.CompletedTask;
		}

		public Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs eventArgs) {
			Robot.ConnectedStateChanged(false);
			return Task.CompletedTask;
		}

		public Task HandleApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs eventArgs) {
			Robot.ApplicationMessageReceived(eventArgs.ApplicationMessage);
			return Task.CompletedTask;
		}
	}
}