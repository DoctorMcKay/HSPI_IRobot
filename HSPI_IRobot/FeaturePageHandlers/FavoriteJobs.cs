using System;
using System.Collections.Generic;
using System.Linq;
using HomeSeer.PluginSdk.Devices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HSPI_IRobot.FeaturePageHandlers {
	public class FavoriteJobs : AbstractFeaturePageHandler {
		protected override string HandleCommand(string cmd, JObject payload) {
			switch (cmd) {
				case "getFavoriteJobs":
					return _getFavoriteJobs();
				
				case "saveJob":
					return _saveJob((string) payload.SelectToken("blid"), (string) payload.SelectToken("name"), (JObject) payload.SelectToken("command"));
				
				case "deleteJob":
					return _deleteJob((string) payload.SelectToken("blid"), (string) payload.SelectToken("name"));
				
				case "getLastCommands":
					return _getLastCommands();
				
				default:
					return BadCmdResponse;
			}
		}

		private string _getFavoriteJobs() {
			List<object> favorites = new List<object>();
			foreach (HsRobot robot in HSPI.Instance.HsRobots) {
				favorites.AddRange(_getFavoriteJobsJson(robot).Select(favJob => new {
					blid = robot.Blid,
					robotName = robot.GetName(),
					job = favJob
				}));
			}
			
			return JsonConvert.SerializeObject(new {favorites});
		}

		private JArray _getFavoriteJobsJson(HsRobot robot) {
			PlugExtraData extraData = (PlugExtraData) HSPI.Instance.GetHsController().GetPropertyByRef(robot.HsDevice.Ref, EProperty.PlugExtraData);
			return !extraData.ContainsNamed("favoritejobs") ? new JArray() : JArray.Parse(extraData["favoritejobs"]);
		}

		private string _saveJob(string blid, string name, JObject command) {
			if (blid == null || string.IsNullOrWhiteSpace(name) || command == null) {
				return BadCmdResponse;
			}

			HsRobot robot = HSPI.Instance.HsRobots.Find(r => r.Blid == blid);
			if (robot == null) {
				return ErrorResponse("Invalid blid");
			}

			if ((string) command.SelectToken("command") != "start") {
				return ErrorResponse("Not a cleaning job");
			}

			JArray favorites = _getFavoriteJobsJson(robot);
			foreach (JToken token in favorites) {
				FavoriteJob favoriteJob = token.ToObject<FavoriteJob>();

				if (favoriteJob.Name == name.Trim()) {
					return ErrorResponse("Job name is already in use for this robot");
				}
				
				if (JToken.DeepEquals(favoriteJob.Command, command)) {
					return ErrorResponse($"Job is identical to \"{favoriteJob.Name}\"");
				}
			}

			bool hasMeaningfulProp = command.Properties()
				.Where(prop => !prop.Value.Equals(JValue.CreateNull()))
				.Any(prop => !new[] {"command", "initiator", "time"}.Contains(prop.Name));

			if (!hasMeaningfulProp) {
				return ErrorResponse("Cannot save a standard cleaning job as a favorite");
			}
			
			favorites.Add(JToken.FromObject(new FavoriteJob {
				Name = name.Trim(),
				Timestamp = (long) DateTime.Now.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
				Command = command
			}));

			PlugExtraData extraData = (PlugExtraData) HSPI.Instance.GetHsController().GetPropertyByRef(robot.HsDevice.Ref, EProperty.PlugExtraData);
			if (!extraData.ContainsNamed("favoritejobs")) {
				extraData.AddNamed("favoritejobs", favorites.ToString());
			} else {
				extraData["favoritejobs"] = favorites.ToString();
			}
			
			HSPI.Instance.GetHsController().UpdatePropertyByRef(robot.HsDevice.Ref, EProperty.PlugExtraData, extraData);
			return SuccessResponse;
		}

		private string _deleteJob(string blid, string name) {
			if (blid == null || string.IsNullOrWhiteSpace(name)) {
				return BadCmdResponse;
			}
			
			HsRobot robot = HSPI.Instance.HsRobots.Find(r => r.Blid == blid);
			if (robot == null) {
				return ErrorResponse("Invalid blid");
			}

			JArray favorites = _getFavoriteJobsJson(robot);
			JToken foundJob = null;
			foreach (JToken token in favorites) {
				FavoriteJob favoriteJob = token.ToObject<FavoriteJob>();
				if (favoriteJob.Name == name.Trim()) {
					foundJob = token;
					break;
				}
			}

			if (foundJob == null) {
				return ErrorResponse("No such job found");
			}

			favorites.Remove(foundJob);

			PlugExtraData extraData = (PlugExtraData) HSPI.Instance.GetHsController().GetPropertyByRef(robot.HsDevice.Ref, EProperty.PlugExtraData);
			extraData["favoritejobs"] = favorites.ToString();
			HSPI.Instance.GetHsController().UpdatePropertyByRef(robot.HsDevice.Ref, EProperty.PlugExtraData, extraData);

			return SuccessResponse;
		}

		private string _getLastCommands() {
			return JsonConvert.SerializeObject(new {
				robots = HSPI.Instance.HsRobots.Select(robot => new {
					blid = robot.Blid,
					name = robot.GetName(),
					lastCommand = robot.Robot?.ReportedState?.SelectToken("lastCommand")
				})
			});
		}

		public struct FavoriteJob {
			[JsonProperty("name")]
			public string Name;
			
			[JsonProperty("timestamp")]
			public long Timestamp;
			
			[JsonProperty("command")]
			public JObject Command;
		}
	}
}