using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using HomeSeer.PluginSdk;
using HomeSeer.PluginSdk.Logging;
using Newtonsoft.Json;

namespace HSPI_IRobot {
	public class AnalyticsClient {
		private const string ReportUrl = "https://hsstats.doctormckay.com/report.php";
		private const string ErrorReportUrl = "https://hsstats.doctormckay.com/error.php";
		private const string DebugReportUrl = "https://hsstats.doctormckay.com/debug_report.php";
		private const string GlobalIniFilename = "DrMcKayGlobal.ini";

		public string CustomSystemId {
			get {
				string customSystemId = _hs.GetINISetting("System", "ID", "", GlobalIniFilename);
				if (customSystemId.Length == 0) {
					customSystemId = Guid.NewGuid().ToString();
					_hs.SaveINISetting("System", "ID", customSystemId, GlobalIniFilename);
				}

				return customSystemId;
			}
		}

		private readonly HSPI _plugin;
		private readonly IHsController _hs;
		private readonly LinkedList<object> _log = new LinkedList<object>();

		public AnalyticsClient(HSPI plugin, IHsController hs) {
			_plugin = plugin;
			_hs = hs;

			if (!Debugger.IsAttached) {
				AppDomain.CurrentDomain.UnhandledException += (_, args) => {
					ReportException((Exception) args.ExceptionObject);
				};

				TaskScheduler.UnobservedTaskException += (_, args) => {
					ReportException(args.Exception);
				};
			}
		}

		private void ReportException(Exception exception) {
			// We want to run this synchronously
			Task.Run(async () => {
				try {
					ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

					HttpClient client = new HttpClient();
					HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, ErrorReportUrl) {
						Content = new StringContent(JsonConvert.SerializeObject(new {
							AnalyticsData = _gatherData(),
							Exception = exception
						}), Encoding.UTF8, "application/json")
					};

					HttpResponseMessage res = await client.SendAsync(req);

					req.Dispose();
					res.Dispose();
					client.Dispose();
				} finally {
					// re-throw the exception
					throw exception;
				}
			}).Wait();
		}

		public void ReportIn(int milliseconds) {
			Timer timer = new Timer(milliseconds) {Enabled = true, AutoReset = false};
			timer.Elapsed += (src, arg) => {
				Report();
			};
		}

		public async void Report() {
			try {
				ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
				
				HttpClient client = new HttpClient();
				HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, ReportUrl) {
					Content = new StringContent(JsonConvert.SerializeObject(_gatherData()), Encoding.UTF8, "application/json")
				};
				HttpResponseMessage res = await client.SendAsync(req);
				_plugin.WriteLog(ELogType.Trace, $"Analytics report: {res.StatusCode}");
				
				req.Dispose();
				res.Dispose();
				client.Dispose();
			} catch (Exception ex) {
				string errMsg = ex.Message;
				Exception inner = ex;
				while ((inner = inner.InnerException) != null) {
					errMsg += $" [{inner.Message}]";
				}
				
				_plugin.WriteLog(ELogType.Trace, $"Analytics report: {errMsg}");
			}
		}

		public void WriteLog(ELogType type, string message, int lineNumber, string caller) {
			_log.AddLast(new LogLine {
				Type = type.ToString(),
				Message = message,
				LineNumber = lineNumber,
				Caller = caller,
				Timestamp = DateTime.Now.ToString(CultureInfo.InvariantCulture)
			});
			
			while (_log.Count > 500) {
				_log.RemoveFirst();
			}
		}

		public async Task<DebugReportResponse> DebugReport(object report) {
			try {
				ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

				using (HttpClient client = new HttpClient()) {
					string jsonReport = JsonConvert.SerializeObject(new {
						AnalyticsData = _gatherData(),
						Log = _log.ToArray(),
						DebugReport = report
					});

					using (HttpResponseMessage res = await client.PostAsync(DebugReportUrl, new StringContent(jsonReport, Encoding.UTF8, "application/json"))) {
						return new DebugReportResponse(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
					}
				}
			} catch (Exception ex) {
				return new DebugReportResponse(false, ex.Message);
			}
		}

		private string _getMonoVersion() {
			Type type = Type.GetType("Mono.Runtime");
			if (type != null) {
				MethodInfo displayName = type.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
				if (displayName != null) {
					return (string) displayName.Invoke(null, null);
				}
			}

			return "";
		}
		
		private AnalyticsData _gatherData() {
			return new AnalyticsData {
				CustomSystemId = CustomSystemId,
				PluginId = _plugin.Id,
				PluginVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
				SystemEnvironmentVersion = Environment.Version.ToString(),
				SystemOsVersion = Environment.OSVersion.ToString(),
				MonoVersion = _getMonoVersion(),
				HsVersion = _hs.Version(),
				HsAppPath = _hs.GetAppPath(),
				HsOsType = _hs.GetOsType(),
				HsEdition = (int) _hs.GetHSEdition()
			};
		}
		
		private struct AnalyticsData {
			public string CustomSystemId;
			public string PluginId;
			public string PluginVersion;
			public string SystemEnvironmentVersion;
			public string SystemOsVersion;
			public string MonoVersion;
			public string HsVersion;
			public string HsAppPath;
			public int HsOsType;
			public int HsEdition;
		}

		private struct LogLine {
			public string Type;
			public string Message;
			public int LineNumber;
			public string Caller;
			public string Timestamp;
		}

		public class DebugReportResponse {
			public readonly bool Success;
			public readonly string Message;

			internal DebugReportResponse(bool success, string message) {
				Success = success;
				Message = message;
			}
		}
	}
}