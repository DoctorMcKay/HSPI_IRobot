using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HSPI_IRobot.FeaturePageHandlers {
	public abstract class AbstractFeaturePageHandler {
		protected string BadCmdResponse => ErrorResponse("Invalid cmd");
		protected string SuccessResponse => JsonConvert.SerializeObject(new {success = true});

		private readonly Dictionary<string, string> _results = new Dictionary<string, string>();

		private static readonly Dictionary<string, AbstractFeaturePageHandler> Handlers = new Dictionary<string, AbstractFeaturePageHandler>();

		protected AbstractFeaturePageHandler() { }

		public static AbstractFeaturePageHandler GetHandler(string page) {
			if (!Handlers.ContainsKey(page)) {
				switch (page) {
					case "robots.html":
						Handlers.Add(page, new ManageRobots());
						break;
					
					case "favorites.html":
						Handlers.Add(page, new FavoriteJobs());
						break;

					default:
						throw new Exception($"Unknown page {page}");
				}
			}

			return Handlers[page];
		}

		public string PostBackProc(string data) {
			JObject payload = JObject.Parse(data);
			string cmd = (string) payload.SelectToken("cmd");
			if (cmd == null) {
				return BadCmdResponse;
			}

			if (cmd.EndsWith("Result")) {
				return ReturnResult(cmd);
			}
			
			if (_results.ContainsKey(cmd)) {
				_results.Remove(cmd);
			}
			
			return HandleCommand(cmd, payload);
		}

		protected abstract string HandleCommand(string cmd, JObject payload);

		protected string ErrorResponse(string error) {
			return JsonConvert.SerializeObject(new {error});
		}

		protected void SetResult(string cmd, object result) {
			SetResult(cmd, JsonConvert.SerializeObject(result));
		}

		protected void SetResult(string cmd, string result) {
			if (_results.ContainsKey(cmd)) {
				_results[cmd] = result;
			} else {
				_results.Add(cmd, result);
			}
		}

		private string ReturnResult(string cmd) {
			string resultType = cmd.Substring(0, cmd.Length - "Result".Length);
			if (!_results.ContainsKey(resultType) || _results[resultType] == null) {
				return BadCmdResponse;
			}

			return _results[resultType];
		}
	}
}