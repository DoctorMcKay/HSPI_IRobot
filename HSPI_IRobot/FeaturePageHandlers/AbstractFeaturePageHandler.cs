using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HSPI_IRobot.FeaturePageHandlers;

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

	public string PostBackProc(string data, string user) {
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
			
		return HandleCommand(cmd, payload, user);
	}

	protected abstract string HandleCommand(string cmd, JObject payload, string user);

	protected string ErrorResponse(string error) {
		return JsonConvert.SerializeObject(new {error});
	}

	protected void SetResult(string cmd, object result) {
		// If a job is likely to take a while (e.g. logging into the iRobot cloud), we can't block the request
		// while we do it. Doing so will block the plugin's connection to HS4, preventing any status updates
		// (or anything else) from going through. So instead let's poll for results to long-running jobs.
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