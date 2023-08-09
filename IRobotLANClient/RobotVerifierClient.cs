using System.Threading.Tasks;
using IRobotLANClient.Enums;
using System.Timers;
using Newtonsoft.Json;

namespace IRobotLANClient {
	public class RobotVerifierClient : RobotClient {
		public RobotType DetectedType { get; private set; } = RobotType.Unrecognized;

		private TaskCompletionSource<RobotType> _taskCompletionSource;
		private bool _receivedStateUpdate = false;

		public RobotVerifierClient(string address, string blid, string password) : base(address, blid, password) { }

		public override bool IsCorrectRobotType() {
			return true;
		}

		protected override void HandleRobotStateUpdate() {
			_receivedStateUpdate = true;
			
			DetectedType = AttemptDetectType();
			if (DetectedType != RobotType.Unrecognized) {
				// Only complete the task if we were actually able to detect the robot type
				// Otherwise, wait for more data
				_taskCompletionSource.TrySetResult(DetectedType);
			}
		}

		private RobotType AttemptDetectType() {
			if (ReportedState.ContainsKey("bin")) {
				return RobotType.Vacuum;
			}

			if (ReportedState.ContainsKey("mopReady") || ReportedState.ContainsKey("tankPresent")) {
				return RobotType.Mop;
			}

			DebugOutput("Call to AttemptDetectType() returned RobotType.Unrecognized - " + JsonConvert.SerializeObject(ReportedState));
			return RobotType.Unrecognized;
		}

		public Task<RobotType> WaitForDetectedType() {
			_taskCompletionSource = new TaskCompletionSource<RobotType>();

			Timer timeout = new Timer {Enabled = true, AutoReset = false, Interval = 10000};
			timeout.Elapsed += (sender, args) => {
				timeout.Dispose();
				DebugOutput($"WaitForDetectedType() elapsed. _receivedStateUpdate = {_receivedStateUpdate}");

				if (_receivedStateUpdate) {
					// If we received any data at all, complete task with robot type Unrecognized
					_taskCompletionSource.TrySetResult(AttemptDetectType());
				} else {
					// We never received a single state update, so cancel the task to report timeout
					_taskCompletionSource.TrySetCanceled();
				}
			};

			return _taskCompletionSource.Task;
		}
	}
}
