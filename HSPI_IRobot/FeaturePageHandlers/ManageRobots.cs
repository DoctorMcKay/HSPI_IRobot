﻿using System.Linq;
using System.Threading.Tasks;
using HomeSeer.PluginSdk.Devices;
using IRobotLANClient;
using IRobotLANClient.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HSPI_IRobot.FeaturePageHandlers {
	public class ManageRobots : AbstractFeaturePageHandler {
		protected override string HandleCommand(string cmd, JObject payload) {
			switch (cmd) {
				case "autodiscover":
					return _autodiscover();

				case "addRobot":
					return _addRobot((string) payload.SelectToken("ip"), (string) payload.SelectToken("blid"), (string) payload.SelectToken("password"));
				
				case "getRobots":
					return _getRobots();
				
				case "getRobotFullStatus":
					return _getRobotFullStatus((string) payload.SelectToken("blid"));
				
				case "cloudLogin":
					return _cloudLogin((string) payload.SelectToken("username"), (string) payload.SelectToken("password"));
				
				case "deleteRobot":
					return _deleteRobot((string) payload.SelectToken("blid"));
				
				case "debugReport":
					return _debugReport();
				
				default:
					return BadCmdResponse;
			}
		}

		private string _autodiscover() {
			RobotDiscovery discovery = new RobotDiscovery();
			discovery.Discover();
			discovery.OnRobotDiscovered += (sender, robot) => {
				SetResult("autodiscover", new {discoveredRobots = discovery.DiscoveredRobots});
			};

			return SuccessResponse;
		}

		private string _addRobot(string ip, string blid, string password) {
			if (ip == null || blid == null || password == null) {
				return BadCmdResponse;
			}
			
			if (HSPI.Instance.HsRobots.Exists(r => r.Blid == blid)) {
				return ErrorResponse("Robot already exists");
			}

			HSPI.Instance.AddNewRobot(ip, blid, password).ContinueWith(task => {
				SetResult("addRobot", task.Result == "OK" ? SuccessResponse : ErrorResponse(task.Result));
			});

			return SuccessResponse;
		}

		private string _getRobots() {
			return JsonConvert.SerializeObject(new {
				robots = HSPI.Instance.HsRobots.Select(robot => new {
					blid = robot.Blid,
					password = robot.Password,
					stateString = robot.StateString,
					ip = robot.ConnectedIp,
					type = robot.Type == RobotType.Vacuum ? "vacuum" : "mop",
					name = robot.GetName(),
					sku = robot.Robot?.Sku ?? "unknown"
				})
			});
		}

		private string _getRobotFullStatus(string blid) {
			if (blid == null) {
				return BadCmdResponse;
			}

			HsRobot robot = HSPI.Instance.HsRobots.Find(r => r.Blid == blid);
			return robot == null
				? ErrorResponse("Invalid blid")
				: JsonConvert.SerializeObject(new {status = robot.Robot?.ReportedState});
		}

		private string _cloudLogin(string username, string password) {
			if (username == null || password == null) {
				return BadCmdResponse;
			}

			RobotCloudAuth cloudAuth = new RobotCloudAuth(username, password);
			cloudAuth.Login().ContinueWith(_ => {
				SetResult("cloudLogin", cloudAuth.LoginError != null
					? ErrorResponse(cloudAuth.LoginError.Message)
					: JsonConvert.SerializeObject(new {robots = cloudAuth.Robots})
				);
			});

			return SuccessResponse;
		}

		private string _deleteRobot(string blid) {
			if (blid == null) {
				return BadCmdResponse;
			}

			HsRobot robot = HSPI.Instance.HsRobots.Find(r => r.Blid == blid);
			if (robot == null) {
				return BadCmdResponse;
			}
			
			robot.Disconnect();
			HSPI.Instance.HsRobots.Remove(robot);

			HsDevice device = HSPI.Instance.GetHsController().GetDeviceByAddress(blid);
			HSPI.Instance.GetHsController().DeleteDevice(device.Ref);
			return SuccessResponse;
		}

		private string _debugReport() {
			// We're going to do this synchronously because it shouldn't happen often
			Task<AnalyticsClient.DebugReportResponse> reportTask = HSPI.Instance.GetAnalyticsClient().DebugReport(new {Robots = HSPI.Instance.HsRobots});
			reportTask.Wait();
			AnalyticsClient.DebugReportResponse response = reportTask.Result;
			return response.Success
				? JsonConvert.SerializeObject(new {report_id = response.Message})
				: JsonConvert.SerializeObject(new {error = response.Message});
		}
	}
}