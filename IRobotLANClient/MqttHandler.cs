using System;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Disconnecting;
using MQTTnet.Client.Receiving;

namespace IRobotLANClient {
	internal class MqttHandler : IMqttClientConnectedHandler, IMqttClientDisconnectedHandler, IMqttApplicationMessageReceivedHandler {
		private readonly Robot _robot;
		private readonly TaskCompletionSource<Exception> _exceptionSource;

		internal MqttHandler(Robot robot) {
			_robot = robot;
			_exceptionSource = new TaskCompletionSource<Exception>();

			_exceptionSource.Task.ContinueWith(task => throw task.Result);
		}

		public Task HandleConnectedAsync(MqttClientConnectedEventArgs eventArgs) {
			// This is going to look ridiculous, but that's only because it is. MQTTnet swallows exceptions thrown in
			// event handlers, because apparently continuing with undefined behavior if an exception isn't handled is
			// preferable to crashing the app. So we have to escape this context to re-throw unhandled exceptions.
			try {
				_robot.ConnectedStateChanged(true);
			} catch (Exception ex) {
				_exceptionSource.SetResult(ex);
			}
			
			return Task.CompletedTask;
		}

		public Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs eventArgs) {
			try {
				_robot.ConnectedStateChanged(false);
			} catch (Exception ex) {
				_exceptionSource.SetResult(ex);
			}

			return Task.CompletedTask;
		}

		public Task HandleApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs eventArgs) {
			try {
				_robot.ApplicationMessageReceived(eventArgs.ApplicationMessage);
			} catch (Exception ex) {
				_exceptionSource.SetResult(ex);
			}

			return Task.CompletedTask;
		}
	}
}