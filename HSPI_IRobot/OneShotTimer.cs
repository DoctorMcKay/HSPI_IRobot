using System;
using System.Timers;

namespace HSPI_IRobot;

public class OneShotTimer {
	private Timer _timer;
	private bool _isDisposed;

	public event EventHandler Elapsed;

	public OneShotTimer(double intervalMilliseconds) {
		_timer = new Timer {Enabled = true, AutoReset = false, Interval = intervalMilliseconds};
		_timer.Elapsed += (sender, args) => {
			Elapsed?.Invoke(this, args);
				
			_isDisposed = true;
			_timer.Dispose();
		};
	}

	public void Stop() {
		if (!_isDisposed) {
			_timer.Stop();
			_timer.Dispose();
			_isDisposed = true;
		}
	}
}