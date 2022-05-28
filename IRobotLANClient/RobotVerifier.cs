﻿using System.Threading.Tasks;
using IRobotLANClient.Enums;
using System.Timers;

namespace IRobotLANClient {
	public class RobotVerifier : Robot {
		public RobotType DetectedType { get; private set; } = RobotType.Unrecognized;

		private TaskCompletionSource<RobotType> _taskCompletionSource;

		public RobotVerifier(string address, string blid, string password) : base(address, blid, password) { }

		public override bool IsCorrectRobotType() {
			return true;
		}

		protected override void HandleRobotStateUpdate() {
			DetectedType = RobotType.Unrecognized;
			
			if (ReportedState.ContainsKey("bin")) {
				DetectedType = RobotType.Vacuum;
			}

			if (ReportedState.ContainsKey("mopReady")) {
				DetectedType = RobotType.Mop;
			}

			_taskCompletionSource.TrySetResult(DetectedType);
		}

		public Task<RobotType> WaitForDetectedType() {
			_taskCompletionSource = new TaskCompletionSource<RobotType>();

			Timer timeout = new Timer {Enabled = true, AutoReset = false, Interval = 10000};
			timeout.Elapsed += (sender, args) => _taskCompletionSource.TrySetCanceled();

			return _taskCompletionSource.Task;
		}
	}
}
