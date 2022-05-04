﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace IRobotLANClient {
	public class RobotDiscovery {
		public event EventHandler<DiscoveredRobot> OnRobotDiscovered;
		public List<DiscoveredRobot> DiscoveredRobots = new List<DiscoveredRobot>();

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
					client.EnableBroadcast = true;
					client.Client.ReceiveTimeout = 5000;
					client.Client.Bind(new IPEndPoint(address.Address, 0));

					Task.Run(() => {
						try {
							while (true) {
								IPEndPoint from = new IPEndPoint(0, 0);
								byte[] receiveBuffer = client.Receive(ref from);
								JObject payload = JObject.Parse(Encoding.UTF8.GetString(receiveBuffer));
								int.TryParse((string) payload.SelectToken("ver"), out int versionNumber);
								DiscoveredRobot robotMetadata = new DiscoveredRobot {
									Version = versionNumber,
									Hostname = (string) payload.SelectToken("hostname"),
									RobotName = (string) payload.SelectToken("robotname"),
									Blid = (string) payload.SelectToken("robotid"),
									IpAddress = (string) payload.SelectToken("ip"),
									MacAddress = (string) payload.SelectToken("mac"),
									SoftwareVersion = (string) payload.SelectToken("sw"),
									Sku = (string) payload.SelectToken("sku")
								};

								DiscoveredRobots.Add(robotMetadata);
								OnRobotDiscovered?.Invoke(this, robotMetadata);
							}
						} catch (SocketException) {
							// Receive probably timed out, so we can close the client now
							client.Dispose();
						}
					});

					byte[] request = Encoding.UTF8.GetBytes("irobotmcs");
					client.Send(request, request.Length, new IPEndPoint(broadcastAddress, 5678));
				}
			}
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