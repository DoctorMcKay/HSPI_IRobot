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

		protected JObject ReportedState = new JObject();
		protected readonly CancellationTokenSource CancellationTokenSource = new CancellationTokenSource();

		private Timer _pingTimer = null;

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
				.Build();

			MqttHandler handler = new MqttHandler(this);
			MqttClient.ApplicationMessageReceivedHandler = handler;
			MqttClient.ConnectedHandler = handler;
			MqttClient.DisconnectedHandler = handler;
			
			#if DEBUG
				Console.WriteLine($"MQTT client connecting to {Address} with blid {Blid}");
			#endif

			MqttClientConnectResult result = await MqttClient.ConnectAsync(clientOptions, CancellationTokenSource.Token);
			ReportedState = new JObject(); // Reset reported state

			return result;
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
				await MqttClient.PublishAsync(msg, CancellationTokenSource.Token);
			} catch (Exception) {
				// We don't want to crash if an exception is raised; the Disconnected handler will fire on its own
			}
		}

		private void EnqueuePing() {
			_pingTimer?.Stop();
			_pingTimer = new Timer {
				AutoReset = false,
				Enabled = true,
				Interval = 10000
			};
			
			// Under normal circumstances, we wouldn't expect the 10s interval to elapse and thus we wouldn't actually
			// send any pings. The "wifistat" topic gets published to very frequently, at least every 10s.
			
			_pingTimer.Elapsed += async (sender, args) => {
				#if DEBUG
					Console.WriteLine("Sending ping");
				#endif

				try {
					await MqttClient.PingAsync(CancellationTokenSource.Token);
					
					#if DEBUG
						Console.WriteLine("Pong received");
					#endif
				} catch (Exception) {
					// We don't have to actually do anything with this exception. The MqttClient will realize that it
					// never received an ack of this transmission and fire the Disconnected handler on its own. We just
					// needed to send something for it to expect an ack.
					
					#if DEBUG
						Console.WriteLine("Ping response failed");
					#endif
				}

				EnqueuePing();
			};
		}

		internal void ApplicationMessageReceived(MqttApplicationMessage msg) {
			EnqueuePing();
			
			string jsonPayload = System.Text.Encoding.UTF8.GetString(msg.Payload);
			JObject payload = JObject.Parse(jsonPayload);

			bool isStatusUpdate = msg.Topic == $"$aws/things/{Blid}/shadow/update";

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

			Name = (string) ReportedState.SelectToken("name");
			Sku = (string) ReportedState.SelectToken("sku");
			BatteryLevel = (byte) ReportedState.SelectToken("batPct");
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
				(string) ReportedState.SelectToken("cleanMissionStatus.cycle"),
				(string) ReportedState.SelectToken("cleanMissionStatus.phase"),
				(int) ReportedState.SelectToken("cleanMissionStatus.error"),
				(int) ReportedState.SelectToken("cleanMissionStatus.notReady"),
				(int) ReportedState.SelectToken("cleanMissionStatus.operatingMode"),
				(string) ReportedState.SelectToken("cleanMissionStatus.initiator")
			));
#endif

			string missionCycle = (string) ReportedState.SelectToken("cleanMissionStatus.cycle");
			string missionPhase = (string) ReportedState.SelectToken("cleanMissionStatus.phase");

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
				
			ErrorCode = (int) ReportedState.SelectToken("cleanMissionStatus.error");
			NotReadyCode = (int) ReportedState.SelectToken("cleanMissionStatus.notReady");
			CanLearnMaps = (bool) ReportedState.SelectToken("pmapLearningAllowed");
			
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
				_pingTimer?.Stop();
				CancellationTokenSource.Cancel(); // also cancel any outstanding comunication
			}
		}

		public JObject GetFullStatus() {
			return ReportedState;
		}

		protected void DebugOutput(string output) {
			OnDebugOutput?.Invoke(this, new DebugOutputEventArgs { Output = output });
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