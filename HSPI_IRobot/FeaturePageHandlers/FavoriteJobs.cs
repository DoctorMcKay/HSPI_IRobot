using System;
using System.Collections.Generic;
using System.Linq;
using HSPI_IRobot.HsEvents;
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
				
				case "getJobUsages":
					return _getJobUsages((string) payload.SelectToken("blid"), (string) payload.SelectToken("name"));
				
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
				favorites.AddRange(robot.GetFavoriteJobs().Select(favJob => new {
					blid = robot.Blid,
					robotName = robot.GetName(),
					job = favJob
				}));
			}
			
			return JsonConvert.SerializeObject(new {favorites});
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

			List<FavoriteJob> favorites = robot.GetFavoriteJobs();
			foreach (FavoriteJob favoriteJob in favorites) {
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
			
			favorites.Add(new FavoriteJob {
				Name = name.Trim(),
				Timestamp = (long) DateTime.Now.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
				Command = command
			});
			
			robot.SaveFavoriteJobs(favorites);
			return SuccessResponse;
		}

		private string _getJobUsages(string blid, string name) {
			if (blid == null || string.IsNullOrWhiteSpace(name)) {
				return BadCmdResponse;
			}
			
			HsRobot robot = HSPI.Instance.HsRobots.Find(r => r.Blid == blid);
			if (robot == null) {
				return ErrorResponse("Invalid blid");
			}

			List<FavoriteJob> favorites = robot.GetFavoriteJobs();
			int idx = favorites.FindIndex(job => job.Name == name);

			if (idx == -1) {
				return ErrorResponse("No such job found");
			}

			string[] usages = HSPI.Instance.GetHsController()
				.GetActionsByInterface(HSPI.Instance.Id)
				.Where(tai => tai.TANumber == StartFavoriteJobAction.ActionNumber)
				.Select(tai => new StartFavoriteJobAction(tai.UID, tai.evRef, tai.DataIn, HSPI.Instance))
				.Where(action => action.ReferencesFavoriteJob(blid, name))
				.Select(action => action.GetEventGroupAndName())
				.ToArray();

			return JsonConvert.SerializeObject(new {usages});
		}

		private string _deleteJob(string blid, string name) {
			if (blid == null || string.IsNullOrWhiteSpace(name)) {
				return BadCmdResponse;
			}
			
			HsRobot robot = HSPI.Instance.HsRobots.Find(r => r.Blid == blid);
			if (robot == null) {
				return ErrorResponse("Invalid blid");
			}

			List<FavoriteJob> favorites = robot.GetFavoriteJobs();
			int idx = favorites.FindIndex(job => job.Name == name);

			if (idx == -1) {
				return ErrorResponse("No such job found");
			}

			favorites.RemoveAt(idx);
			robot.SaveFavoriteJobs(favorites);
			return SuccessResponse;
		}

		private string _getLastCommands() {
			return JsonConvert.SerializeObject(new {
				robots = HSPI.Instance.HsRobots.Select(robot => new {
					blid = robot.Blid,
					name = robot.GetName(),
					lastCommand = robot.Robot?.LastJobStartCommand
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