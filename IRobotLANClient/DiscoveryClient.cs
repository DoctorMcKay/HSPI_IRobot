using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace IRobotLANClient {
	public class DiscoveryClient {
		public event EventHandler<DiscoveredRobot> OnRobotDiscovered;
		public List<DiscoveredRobot> DiscoveredRobots = new List<DiscoveredRobot>();

		private readonly byte[] _discoveryPacket = Encoding.UTF8.GetBytes("irobotmcs");
		private const short DiscoveryPort = 5678;

		public async Task<DiscoveredRobot> GetRobotPublicDetails(string address) {
			using (UdpClient client = new UdpClient()) {
				client.Client.ReceiveTimeout = 5000;

				try {
					client.Connect(IPAddress.Parse(address), DiscoveryPort);
					await client.SendAsync(_discoveryPacket, _discoveryPacket.Length);
					return _parseRobot((await client.ReceiveAsync()).Buffer);
				} catch (Exception) {
					return null;
				}
			}
		}

		public void Discover() {
			foreach (NetworkInterface iface in NetworkInterface.GetAllNetworkInterfaces()) {
				if (iface.OperationalStatus != OperationalStatus.Up) {
					continue;
				}

				foreach (UnicastIPAddressInformation address in iface.GetIPProperties().UnicastAddresses) {
					if (address.Address.AddressFamily != AddressFamily.InterNetwork) {
						// Only support IPv4 right now
						continue;
					}

					uint addressLong = BitConverter.ToUInt32(address.Address.GetAddressBytes(), 0);
					uint netMask = BitConverter.ToUInt32(address.IPv4Mask.GetAddressBytes(), 0);
					uint broadcastAddress = addressLong | ~netMask;

					UdpClient client = new UdpClient();
					try {
						client.EnableBroadcast = true;
						client.Client.ReceiveTimeout = 5000;
						client.Client.Bind(new IPEndPoint(address.Address, 0));
					} catch (SocketException) {
						// Can't bind to this address
						continue;
					}

					bool disposed = false;
					
					DateTime startTime = DateTime.Now;
					Task.Run(async () => {
						while (!disposed && DateTime.Now.Subtract(startTime).TotalSeconds <= 4) {
							client.Send(_discoveryPacket, _discoveryPacket.Length, new IPEndPoint(broadcastAddress, DiscoveryPort));
							await Task.Delay(1000);
						}
					});

					Task.Run(() => {
						try {
							while (true) {
								IPEndPoint from = new IPEndPoint(0, 0);
								byte[] receiveBuffer = client.Receive(ref from);

								DiscoveredRobot robotMetadata = _parseRobot(receiveBuffer);
								if (robotMetadata == null || DiscoveredRobots.Exists(r => r.Blid == robotMetadata.Blid)) {
									continue;
								}

								DiscoveredRobots.Add(robotMetadata);
								OnRobotDiscovered?.Invoke(this, robotMetadata);
							}
						} catch (SocketException) {
							// Receive probably timed out, so we can close the client now
							disposed = true;
							client.Dispose();
						}
					});
				}
			}
		}

		private DiscoveredRobot _parseRobot(byte[] receiveBuffer) {
			JObject payload = JObject.Parse(Encoding.UTF8.GetString(receiveBuffer));
			int.TryParse((string) payload.SelectToken("ver"), out int versionNumber);

			string blid = (string) payload.SelectToken("robotid");
			string hostname = (string) payload.SelectToken("hostname");
			if (blid == null && hostname == null) {
				return null; // malformed, apparently
			}
								
			if (blid == null) {
				blid = hostname.Split('-')[1];
			}

			return new DiscoveredRobot {
				Version = versionNumber,
				Hostname = hostname,
				RobotName = (string) payload.SelectToken("robotname"),
				Blid = blid,
				IpAddress = (string) payload.SelectToken("ip"),
				MacAddress = (string) payload.SelectToken("mac"),
				SoftwareVersion = (string) payload.SelectToken("sw"),
				Sku = (string) payload.SelectToken("sku")
			};
		}

		public class DiscoveredRobot {
			public int Version { get; internal set; }
			public string Hostname { get; internal set; }
			public string RobotName { get; internal set; }
			public string Blid { get; internal set; }
			public string IpAddress { get; internal set; }
			public string MacAddress { get; internal set; }
			public string SoftwareVersion { get; internal set; }
			public string Sku { get; internal set; }
		}
	}
}