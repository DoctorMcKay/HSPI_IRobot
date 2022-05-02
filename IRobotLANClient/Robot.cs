using System;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using IRobotLANClient.Enums;
using MQTTnet.Client;
using MQTTnet;
using MQTTnet.Client.Options;
using MQTTnet.Formatter;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

		protected IMqttClient MqttClient;
		protected readonly string Address;
		protected readonly string Blid;
		protected readonly string Password;

		protected JObject ReportedState = new JObject();

		public Robot(string address, string blid, string password) {
			Connected = false;
			Address = address;
			Blid = blid;
			Password = password;
		}
		
		public async Task Connect() {
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

			await MqttClient.ConnectAsync(clientOptions, CancellationToken.None);
		}

		protected async void SendCommand(string command) {
			DateTime unixEpoch = new DateTime(1970, 1, 1);

			string payload = JsonConvert.SerializeObject(new {
				command,
				time = (long) DateTime.Now.Subtract(unixEpoch).TotalMilliseconds,
				initiator = "localApp"
			});

			await MqttClient.PublishAsync("cmd", payload);
		}

		internal void ApplicationMessageReceived(MqttApplicationMessage msg) {
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
				
			foreach (JProperty prop in stateUpdate.Properties()) {
				ReportedState[prop.Name] = prop.Value;
			}
				
			Name = (string) ReportedState.SelectToken("name");
			Sku = (string) ReportedState.SelectToken("sku");
			BatteryLevel = (byte) ReportedState.SelectToken("batPct");
			ChildLock = (bool) (ReportedState.SelectToken("childLock") ?? JToken.FromObject(false));
				
#if DEBUG
			/*
					 * Observed cycle values: none, clean, spot, dock (sent to base without a job), evac, train (mapping run)
					 * Observed phase values: charge, run, stuck, stop, hmUsrDock (user sent home), hmPostMsn (returning to dock after mission), evac
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
					Console.WriteLine($"WARNING: Unknown mission cycle {missionCycle}");
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
					
				case "evac":
					Phase = MissionPhase.Evac;
					break;
					
				default:
					Console.WriteLine($"WARNING: Unknown mission phase {missionPhase}");
					Phase = MissionPhase.Unknown;
					break;
			}
				
			ErrorCode = (int) ReportedState.SelectToken("cleanMissionStatus.error");
			NotReadyCode = (int) ReportedState.SelectToken("cleanMissionStatus.notReady");
			
			HandleRobotStateUpdate();
		}

		protected abstract void HandleRobotStateUpdate();

		internal void ConnectedStateChanged(bool connected) {
			Connected = connected;
			
			#if DEBUG
				Console.WriteLine("MQTT client " + (connected ? "connected" : "disconnected"));
			#endif
		}

		public JObject GetFullStatus() {
			return ReportedState;
		}
	}
}