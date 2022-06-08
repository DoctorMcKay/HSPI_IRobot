using System;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IRobotLANClient.Enums;
using IRobotLANClient.Exceptions;
using IRobotLANClient.JsonObjects;
using MQTTnet.Client;
using MQTTnet;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Options;
using MQTTnet.Formatter;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Timer = System.Timers.Timer;

namespace IRobotLANClient {
	public abstract class RobotClient {
		public bool Connected { get; private set; }
		public JObject ReportedState { get; private set; } = new JObject();
		public JObject LastJobStartCommand { get; private set; } = null;
		public string Name { get; private set; }
		public string Sku { get; private set; }
		public byte BatteryLevel { get; private set; }
		public ChargeLightRingPattern ChargeLightRingPattern { get; private set; }
		public bool ChildLock { get; private set; }
		public MissionCycle Cycle { get; private set; }
		public MissionPhase Phase { get; private set; }
		public int ErrorCode { get; private set; }
		public int NotReadyCode { get; private set; }
		public bool CanLearnMaps { get; private set; }
		public byte SoftwareUpdateDownloadProgress { get; private set; }
		public string SoftwareVersion { get; private set; }

		public event EventHandler OnConnected;
		public event EventHandler OnDisconnected;
		public event EventHandler OnStateUpdated;
		public event EventHandler<UnexpectedValueEventArgs> OnUnexpectedValue;
		public event EventHandler<DebugOutputEventArgs> OnDebugOutput;

		protected IMqttClient MqttClient;
		protected readonly string Address;
		protected readonly string Blid;
		protected readonly string Password;

		private string MqttStatusTopic => $"$aws/things/{Blid}/shadow/update";

		private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
		private bool _awaitingFirstReport;
		private bool _awaitingFirstReportTimer;
		private DateTime _connectedTime;

		#if DEBUG
		private static bool SpoofingSoftwareUpdate;
		#endif

		public RobotClient(string address, string blid, string password) {
			Connected = false;
			Address = address;
			Blid = blid;
			Password = password;
		}

		public async Task<MqttClientConnectResult> Connect() {
			MqttFactory factory = new MqttFactory();
			MqttClient = factory.CreateMqttClient();

			MqttClientOptionsBuilderTlsParameters tlsParams = new MqttClientOptionsBuilderTlsParameters {
				AllowUntrustedCertificates = true,
				CertificateValidationHandler = _ => true,
				SslProtocol = SslProtocols.Tls12,
				UseTls = true
			};

			IMqttClientOptions clientOptions = new MqttClientOptionsBuilder()
				.WithTcpServer(Address, 8883)
				.WithCredentials(Blid, Password)
				.WithClientId(Blid)
				.WithTls(tlsParams)
				.WithProtocolVersion(MqttProtocolVersion.V311)
				.WithKeepAlivePeriod(TimeSpan.FromSeconds(5))
				.WithCommunicationTimeout(TimeSpan.FromSeconds(5))
				.Build();

			MqttHandler handler = new MqttHandler(this);
			MqttClient.ApplicationMessageReceivedHandler = handler;
			MqttClient.ConnectedHandler = handler;
			MqttClient.DisconnectedHandler = handler;

			#if DEBUG
			if (SpoofingSoftwareUpdate) {
				throw new Exception("Spoofing software update");
			}
			
			Console.WriteLine($"MQTT client connecting to {Address} with blid {Blid}");
			#endif

			_awaitingFirstReport = true;

			Timer connectTimeout = new Timer {
				Enabled = true,
				AutoReset = false,
				Interval = 10000
			};
			
			connectTimeout.Elapsed += (sender, args) => {
				SignalCancellation();
				// ReSharper disable once AccessToDisposedClosure
				connectTimeout.Dispose();
			};

			DateTime connectStartTime = DateTime.Now;
			try {
				MqttClientConnectResult result = await MqttClient.ConnectAsync(clientOptions, _cancellationTokenSource.Token);
				connectTimeout.Stop();
				connectTimeout.Dispose();

				_connectedTime = DateTime.Now;

				// Subscribing to the status topic isn't strictly necessary as the robot sends us those updates by default,
				// but let's subscribe anyway just to be a good citizen
				await MqttClient.SubscribeAsync(MqttStatusTopic);
				ReportedState = new JObject(); // Reset reported state

				return result;
			} catch (OperationCanceledException) {
				throw new Exception($"Connection timed out after {DateTime.Now.Subtract(connectStartTime).TotalMilliseconds} milliseconds");
			} catch (Exception ex) {
				for (Exception checkException = ex; checkException != null; checkException = checkException.InnerException) {
					if (checkException.Message.Contains("BadUserNameOrPassword")) {
						throw new RobotConnectionException("Robot password is incorrect", ConnectionError.IncorrectCredentials, ex);
					}

					if (checkException.Message.Contains("actively refused it")) {
						throw new RobotConnectionException("Connection refused", ConnectionError.ConnectionRefused, ex);
					}

					if (checkException.Message.Contains("timed out")) {
						throw new RobotConnectionException("Connection timed out", ConnectionError.ConnectionTimedOut, ex);
					}
				}

				throw new RobotConnectionException("Unspecified connection error", ConnectionError.UnspecifiedError, ex);
			}
		}

		public async Task Disconnect() {
			if (!MqttClient.IsConnected) {
				return; // nothing to do
			}

			await MqttClient.DisconnectAsync();
		}

		public abstract bool IsCorrectRobotType();

		public async Task<bool> WaitForTypeValidation(int timeoutMilliseconds) {
			DateTime start = DateTime.Now;

			while (DateTime.Now.Subtract(start).TotalMilliseconds < timeoutMilliseconds) {
				if (IsCorrectRobotType()) {
					return true;
				}

				await Task.Delay(100);
			}

			// Validation timed out
			return false;
		}

		public void Clean() {
			SendCommand("start");
		}

		public void CleanCustom(JObject command) {
			SendCommand("start", command);
		}

		public void Stop() {
			SendCommand("stop");
		}

		public void Pause() {
			SendCommand("pause");
		}

		public void Resume() {
			SendCommand("resume");
		}

		public void Dock() {
			SendCommand("dock");
		}

		public void Find() {
			SendCommand("find");
		}

		public void Train() {
			SendCommand("train");
		}

		public void Reboot() {
			SendCommand("reset");
		}

		protected async void SendCommand(string command, JObject commandParams = null) {
			DateTime unixEpoch = new DateTime(1970, 1, 1);

			JObject cmd = commandParams != null ? (JObject) commandParams.DeepClone() : new JObject();
			cmd["command"] = command;
			cmd["time"] = (long) DateTime.Now.Subtract(unixEpoch).TotalSeconds;
			cmd["initiator"] = "localApp";

			MqttApplicationMessage msg = new MqttApplicationMessage {
				Topic = "cmd",
				Payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(cmd))
			};

			try {
				await MqttClient.PublishAsync(msg, _cancellationTokenSource.Token);
			} catch (Exception) {
				// We don't want to crash if an exception is raised; the Disconnected handler will fire on its own
			}
		}

		public virtual bool SupportsConfigOption(ConfigOption option) {
			// All of these checks are based on my limited vision into i7 and 890
			switch (option) {
				case ConfigOption.ChargeLightRingPattern:
					return (int) (ReportedState.SelectToken("featureFlags.chrgLrPtrnEnable") ?? JToken.FromObject(0)) == 1;

				case ConfigOption.ChildLock:
					return (int) (ReportedState.SelectToken("featureFlags.childLockEnable") ?? JToken.FromObject(0)) == 1;

				default:
					return false;
			}
		}

		public void SetChargeLightRingPattern(ChargeLightRingPattern pattern) {
			UpdateOption(new {chrgLrPtrn = (int) pattern});
		}

		public void SetChildLock(bool childLock) {
			UpdateOption(new {childLock});
		}

		#if DEBUG
		public void SpoofSoftwareUpdate() {
			Console.WriteLine("Spoofing software update");

			SpoofingSoftwareUpdate = true;

			SoftwareUpdateDownloadProgress = 0;
			Timer timer = new Timer {AutoReset = true, Enabled = true, Interval = 500};
			timer.Elapsed += async (sender, args) => {
				SoftwareUpdateDownloadProgress++;
				OnStateUpdated?.Invoke(this, null);

				if (SoftwareUpdateDownloadProgress >= 100) {
					timer.Stop();
					timer.Dispose();
					
					await Disconnect();
					await Task.Delay(30000);
					SpoofingSoftwareUpdate = false;
				}
			};
		}
		#endif

		protected void UpdateOption(object request) {
			MqttApplicationMessage msg = new MqttApplicationMessage {
				Topic = "delta",
				Payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new {state = request}))
			};

			try {
				MqttClient.PublishAsync(msg, _cancellationTokenSource.Token).Wait();
			} catch (Exception) {
				// We don't want to crash if an exception is raised; the Disconnected handler will fire on its own
			}
		}

		internal void ApplicationMessageReceived(MqttApplicationMessage msg) {
			string jsonPayload = Encoding.UTF8.GetString(msg.Payload);
			JObject payload = JObject.Parse(jsonPayload);

			bool isStatusUpdate = msg.Topic == MqttStatusTopic;

			#if DEBUG
			if (!isStatusUpdate && msg.Topic != "wifistat" && msg.Topic != "logUpload") {
				Console.WriteLine($"Received message with topic {msg.Topic}");
			}
			#endif

			if (!isStatusUpdate) {
				return;
			}

			// This is a robot status update
			JObject stateUpdate = (JObject) payload.SelectToken("state.reported");
			if (stateUpdate == null) {
				// This shouldn't ordinarily happen, but in case it does...
				return;
			}

			// First let's find the differences between the two objects for debugging purposes
			JObject previousReportedState = JObject.Parse(ReportedState.ToString());

			foreach (JProperty prop in stateUpdate.Properties()) {
				ReportedState[prop.Name] = prop.Value;
			}

			foreach (string change in JsonCompare.Compare(previousReportedState, ReportedState)) {
				DebugOutput(change);
			}

			// Some robots don't send their entire state all at once upon connecting, which can cause problems since
			// a lot of our code expects the entire state to be there once we receive a report. So let's wait 1 second
			// after receiving the first report for additional reports to come in.

			if (_awaitingFirstReport) {
				DebugOutput($"Received first report after {DateTime.Now.Subtract(_connectedTime).TotalMilliseconds} ms");
				_awaitingFirstReport = false;
				_awaitingFirstReportTimer = true;

				Task.Run(async () => {
					await Task.Delay(1000);
					_awaitingFirstReportTimer = false;
					ProcessStateUpdate();
				});
			}

			if (!_awaitingFirstReportTimer) {
				ProcessStateUpdate();
			}
		}

		private void ProcessStateUpdate() {
			ReportedState state = JsonConvert.DeserializeObject<ReportedState>(ReportedState.ToString());
			Name = state.Name;
			Sku = state.Sku;
			BatteryLevel = state.BatPct;
			ChildLock = state.ChildLock;

			// This will default to DockingAndCharging if the robot doesn't support light ring patterns, so it's up to
			// the consumer to check that the robot supports the feature before actually trusting this value.
			ChargeLightRingPattern = (ChargeLightRingPattern) state.ChrgLrPtrn;

			#if DEBUG
			/*
			 * Observed cycle values: none, clean, spot, dock (sent to base without a job), evac, train (mapping run)
			 * Observed phase values: charge, run, stuck, stop, hmUsrDock (user sent home), hmPostMsn (returning to dock after mission), hmMidMsn (returning to dock mid mission), evac, chargingerror
			 * Notable values: none/charge (on base, no job), evac/evac (emptying bin but no job), clean/run, none/stop (off base, no job)
			 */
			Console.WriteLine(string.Format(
				"Battery: {0}%, Cycle: {1}, Phase: {2}, Error: {3}, NotReady: {4}, OperatingMode: {5}, Initiator: {6}",
				BatteryLevel,
				(string) (ReportedState.SelectToken("cleanMissionStatus.cycle") ?? ""),
				(string) (ReportedState.SelectToken("cleanMissionStatus.phase") ?? ""),
				(int) (ReportedState.SelectToken("cleanMissionStatus.error") ?? 0),
				(int) (ReportedState.SelectToken("cleanMissionStatus.notReady") ?? 0),
				(int) (ReportedState.SelectToken("cleanMissionStatus.operatingMode") ?? 0),
				(string) (ReportedState.SelectToken("cleanMissionStatus.initiator") ?? 0)
			));
			#endif

			string missionCycle = state.CleanMissionStatus?.Cycle ?? "none";
			string missionPhase = state.CleanMissionStatus?.Phase ?? "stop";

			switch (missionCycle) {
				case "none":
					Cycle = MissionCycle.None;
					break;

				case "clean":
					Cycle = MissionCycle.Clean;
					break;

				case "spot":
					Cycle = MissionCycle.Spot;
					break;

				case "dock":
					Cycle = MissionCycle.Dock;
					break;

				case "evac":
					Cycle = MissionCycle.Evac;
					break;

				case "train":
					Cycle = MissionCycle.Train;
					break;

				default:
					OnUnexpectedValue?.Invoke(this, new UnexpectedValueEventArgs {ValueType = "MissionCycle", Value = missionCycle});
					Cycle = MissionCycle.Unknown;
					break;
			}

			switch (missionPhase) {
				case "charge":
					Phase = MissionPhase.Charge;
					break;

				case "run":
					Phase = MissionPhase.Run;
					break;

				case "stuck":
					Phase = MissionPhase.Stuck;
					break;

				case "stop":
					Phase = MissionPhase.Stop;
					break;

				case "hmUsrDock":
					Phase = MissionPhase.UserSentHome;
					break;

				case "hmPostMsn":
					Phase = MissionPhase.DockingAfterMission;
					break;

				case "hmMidMsn":
					Phase = MissionPhase.DockingMidMission;
					break;

				case "evac":
					Phase = MissionPhase.Evac;
					break;

				case "chargingerror":
					Phase = MissionPhase.ChargingError;
					break;

				default:
					OnUnexpectedValue?.Invoke(this, new UnexpectedValueEventArgs {ValueType = "MissionPhase", Value = missionPhase});
					Phase = MissionPhase.Unknown;
					break;
			}

			ErrorCode = state.CleanMissionStatus?.Error ?? 0;
			NotReadyCode = state.CleanMissionStatus?.NotReady ?? 0;
			CanLearnMaps = state.PmapLearningAllowed;
			SoftwareVersion = state.SoftwareVer;

			#if DEBUG
			if (!SpoofingSoftwareUpdate) {
				SoftwareUpdateDownloadProgress = state.OtaDownloadProgress;
			}
			#else
			SoftwareUpdateDownloadProgress = state.OtaDownloadProgress;
			#endif

			if (ReportedState.SelectToken("lastCommand.command")?.Value<string>() == "start") {
				LastJobStartCommand = ReportedState.SelectToken("lastCommand")?.Value<JObject>();
			}

			HandleRobotStateUpdate();

			OnStateUpdated?.Invoke(this, null);
		}

		protected abstract void HandleRobotStateUpdate();

		internal void ConnectedStateChanged(bool connected) {
			Connected = connected;

			#if DEBUG
			Console.WriteLine("MQTT client " + (connected ? "connected" : "disconnected"));
			#endif

			if (connected) {
				OnConnected?.Invoke(this, null);
			} else {
				OnDisconnected?.Invoke(this, null);
				SignalCancellation(); // also cancel any outstanding communication
			}
		}

		protected void DebugOutput(string output) {
			OnDebugOutput?.Invoke(this, new DebugOutputEventArgs {Output = output});
		}

		private async void SignalCancellation() {
			CancellationTokenSource tokenSource = _cancellationTokenSource;
			tokenSource.Cancel();
			
			_cancellationTokenSource = new CancellationTokenSource();

			// Wait 2 minutes before disposing the old cancellation token source
			await Task.Delay(120000);
			tokenSource.Dispose();
		}

		public class UnexpectedValueEventArgs : EventArgs {
			public string ValueType { get; internal set; }
			public string Value { get; internal set; }
		}

		public class DebugOutputEventArgs : EventArgs {
			public string Output { get; internal set; }
		}
	}
}