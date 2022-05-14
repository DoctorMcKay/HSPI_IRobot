using System;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IRobotLANClient.Enums;
using MQTTnet.Client;
using MQTTnet;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Options;
using MQTTnet.Formatter;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Timer = System.Timers.Timer;

namespace IRobotLANClient {
	public abstract class Robot {
		public bool Connected { get; private set; }
		public JObject ReportedState { get; private set; } = new JObject();
		public string Name { get; private set; }
		public string Sku { get; private set; }
		public byte BatteryLevel { get; private set; }
		public bool ChildLock { get; private set; }
		public MissionCycle Cycle { get; private set; }
		public MissionPhase Phase { get; private set; }
		public int ErrorCode { get; private set; }
		public int NotReadyCode { get; private set; }
		public bool CanLearnMaps { get; private set; }

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
		private Timer _startupTimer;

		public Robot(string address, string blid, string password) {
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
			};

			DateTime connectStartTime = DateTime.Now;
			try {
				MqttClientConnectResult result = await MqttClient.ConnectAsync(clientOptions, _cancellationTokenSource.Token);
				connectTimeout.Stop();
				
				// Subscribing to the status topic isn't strictly necessary as the robot sends us those updates by default,
				// but let's subscribe anyway just to be a good citizen
				await MqttClient.SubscribeAsync(MqttStatusTopic);
				ReportedState = new JObject(); // Reset reported state

				return result;
			} catch (OperationCanceledException) {
				throw new Exception($"Connection timed out after {DateTime.Now.Subtract(connectStartTime).TotalMilliseconds} milliseconds");
			}
		}

		public async Task Disconnect() {
			if (!MqttClient.IsConnected) {
				return; // nothing to do
			}

			await MqttClient.DisconnectAsync();
		}

		public abstract bool IsCorrectRobotType();

		public void Clean() {
			SendCommand("start");
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

		protected async void SendCommand(string command) {
			DateTime unixEpoch = new DateTime(1970, 1, 1);

			string payload = JsonConvert.SerializeObject(new {
				command,
				time = (long) DateTime.Now.Subtract(unixEpoch).TotalSeconds,
				initiator = "localApp"
			});

			MqttApplicationMessage msg = new MqttApplicationMessage() {
				Topic = "cmd",
				Payload = Encoding.UTF8.GetBytes(payload)
			};

			try {
				await MqttClient.PublishAsync(msg, _cancellationTokenSource.Token);
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
				_awaitingFirstReport = false;
				
				_startupTimer = new Timer {
					AutoReset = false,
					Enabled = true,
					Interval = 1000
				};

				_startupTimer.Elapsed += (sender, args) => {
					_startupTimer = null;
					ProcessStateUpdate();
				};
			}

			if (_startupTimer == null) {
				ProcessStateUpdate();
			}
		}
		
		private void ProcessStateUpdate() {
			Name = (string) (ReportedState.SelectToken("name") ?? "");
			Sku = (string) (ReportedState.SelectToken("sku") ?? "");
			BatteryLevel = (byte) (ReportedState.SelectToken("batPct") ?? 0);
			ChildLock = (bool) (ReportedState.SelectToken("childLock") ?? JToken.FromObject(false));
				
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

			string missionCycle = (string) (ReportedState.SelectToken("cleanMissionStatus.cycle") ?? "none");
			string missionPhase = (string) (ReportedState.SelectToken("cleanMissionStatus.phase") ?? "stop");

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
				
			ErrorCode = (int) (ReportedState.SelectToken("cleanMissionStatus.error") ?? 0);
			NotReadyCode = (int) (ReportedState.SelectToken("cleanMissionStatus.notReady") ?? 0);
			CanLearnMaps = (bool) (ReportedState.SelectToken("pmapLearningAllowed") ?? false);
			
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
			OnDebugOutput?.Invoke(this, new DebugOutputEventArgs { Output = output });
		}

		private void SignalCancellation() {
			_cancellationTokenSource.Cancel();
			_cancellationTokenSource.Dispose();
			_cancellationTokenSource = new CancellationTokenSource();
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