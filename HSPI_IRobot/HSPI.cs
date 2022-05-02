using System;
using System.Runtime.CompilerServices;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk;
using HomeSeer.PluginSdk.Logging;

namespace HSPI_IRobot {
	public class HSPI : AbstractPlugin {
		public override string Name { get; } = "iRobot";
		public override string Id { get; } = "iRobot";

		private bool _debugLogging;

		protected override void Initialize() {
			WriteLog(ELogType.Debug, "Initializing");
			
			AnalyticsClient analytics = new AnalyticsClient(this, HomeSeerSystem);
			
			// Build the settings page
			PageFactory settingsPageFactory = PageFactory
				.CreateSettingsPage("iRobotSettings", "iRobot Settings")
				.WithLabel("plugin_status", "Status (refresh to update)", "x")
				.WithGroup("debug_group", "<hr>", new AbstractView[] {
					new LabelView("debug_support_link", "Documentation", "<a href=\"https://github.com/DoctorMcKay/HSPI_iRobot/blob/master/README.md\" target=\"_blank\">GitHub</a>"), 
					new LabelView("debug_donate_link", "Fund Future Development", "This plugin is and always will be free.<br /><a href=\"https://github.com/sponsors/DoctorMcKay\" target=\"_blank\">Please consider donating to fund future development.</a>"),
					new LabelView("debug_system_id", "System ID (include this with any support requests)", analytics.CustomSystemId),
					#if DEBUG
						new LabelView("debug_log", "Enable Debug Logging", "ON - DEBUG BUILD")
					#else
						new ToggleView("debug_log", "Enable Debug Logging")
					#endif
				});

			Settings.Add(settingsPageFactory.Page);
			
			HomeSeerSystem.RegisterDeviceIncPage(Id, "robots.html", "Manage Robots");
			
			analytics.ReportIn(5000);
		}
		
		protected override void OnSettingsLoad() {
			// Called when the settings page is loaded. Use to pre-fill the inputs.
			string statusText = Status.Status.ToString().ToUpper();
			if (Status.StatusText.Length > 0) {
				statusText += ": " + Status.StatusText;
			}
			((LabelView) Settings.Pages[0].GetViewById("plugin_status")).Value = statusText;
		}

		protected override bool OnSettingChange(string pageId, AbstractView currentView, AbstractView changedView) {
			WriteLog(ELogType.Debug, $"Request to save setting {currentView.Id} on page {pageId}");

			if (pageId != "iRobotSettings") {
				WriteLog(ELogType.Warning, $"Request to save settings on unknown page {pageId}!");
				return true;
			}

			switch (currentView.Id) {
				case "debug_log":
					_debugLogging = changedView.GetStringValue() == "True";
					return true;
			}
			
			WriteLog(ELogType.Info, $"Request to save unknown setting {currentView.Id}");
			return false;
		}

		protected override void BeforeReturnStatus() {
			
		}
		
		public void WriteLog(ELogType logType, string message, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string caller = null) {
#if DEBUG
			bool isDebugMode = true;

			// Prepend calling function and line number
			message = $"[{caller}:{lineNumber}] {message}";
			
			// Also print to console in debug builds
			string type = logType.ToString().ToLower();
			Console.WriteLine($"[{type}] {message}");
#else
			if (logType == ELogType.Trace) {
				// Don't record Trace events in production builds even if debug logging is enabled
				return;
			}

			bool isDebugMode = _debugLogging;
#endif

			if (logType <= ELogType.Debug && !isDebugMode) {
				return;
			}
			
			HomeSeerSystem.WriteLog(logType, message, Name);
		}
	}
}