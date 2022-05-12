using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HSPI_IRobot {
	public class RobotCloudAuth {
		public bool LoginInProcess { get; private set; }
		public List<RobotDetails> Robots { get; private set; }
		public Exception LoginError { get; private set; }
		
		protected readonly string Username;
		protected readonly string Password;

		public RobotCloudAuth(string username, string password, string apiKey = null) {
			Username = username;
			Password = password;

			LoginInProcess = false;
		}

		public async void Login() {
			LoginInProcess = true;
			LoginError = null;
			Robots = new List<RobotDetails>();

			try {
				using (HttpClient client = new HttpClient()) {
					JObject endpoints = JObject.Parse(await client.GetStringAsync("https://disc-prod.iot.irobotapi.com/v1/discover/endpoints?country_code=US"));

					string irobotDomain = (string) endpoints.SelectToken("deployments.v011.httpBase");
					string gigyaApiKey = (string) endpoints.SelectToken("gigya.api_key");
					string gigyaDomain = (string) endpoints.SelectToken("gigya.datacenter_domain");
					DebugLog($"Using Gigya domain {gigyaDomain} and API key {gigyaApiKey}");

					// Gigya login
					Dictionary<string, string> postFields = new Dictionary<string, string> {
						{ "apiKey", gigyaApiKey },
						{ "format", "json" },
						{ "loginID", Username },
						{ "loginMode", "standard" },
						{ "password", Password },
						{ "targetEnv", "mobile" }
					};

					string gigyaUid, gigyaUidSignature, gigyaSignatureTimestamp;

					using (HttpResponseMessage response = await client.PostAsync($"https://accounts.{gigyaDomain}/accounts.login", new FormUrlEncodedContent(postFields))) {
						if (!response.IsSuccessStatusCode) {
							throw new Exception($"Login error {response.StatusCode}");
						}

						JObject gigyaLoginResponse = JObject.Parse(await response.Content.ReadAsStringAsync());
						gigyaUid = (string) gigyaLoginResponse.SelectToken("UID");
						gigyaUidSignature = (string) gigyaLoginResponse.SelectToken("UIDSignature");
						gigyaSignatureTimestamp = (string) gigyaLoginResponse.SelectToken("signatureTimestamp");
						DebugLog($"Gigya login success with uid {gigyaUid}");
					}

					string irobotLoginBody = JsonConvert.SerializeObject(new {
						app_id = "ANDROID-3EDCAF0A-0881-4DD8-8849-B18833F15DC1",
						assume_robot_ownership = "0",
						gigya = new {
							uid = gigyaUid,
							signature = gigyaUidSignature,
							timestamp = gigyaSignatureTimestamp
						}
					});

					using (
						HttpResponseMessage response = await client.PostAsync(
							$"{irobotDomain}/v2/login",
							new StringContent(irobotLoginBody, Encoding.UTF8, "application/json")
						)
					) {
						if (!response.IsSuccessStatusCode) {
							throw new Exception($"iRobot login error {response.StatusCode}");
						}

						JObject irobotLoginResponse = JObject.Parse(await response.Content.ReadAsStringAsync());
						JObject robots = (JObject) irobotLoginResponse.SelectToken("robots");
						if (robots == null) {
							throw new Exception("No robots found");
						}

						foreach (JProperty prop in robots.Properties()) {
							RobotDetails details = new RobotDetails(prop.Name, prop.Value);
							Robots.Add(details);
							DebugLog($"Found robot {details.Blid} / {details.Password}");
						}
					}
				}
			} catch (Exception ex) {
				LoginError = ex;
				DebugLog(ex.ToString());
			} finally {
				LoginInProcess = false;
			}
		}

		private void DebugLog(string content) {
			#if DEBUG
				Console.WriteLine($"[RobotCloudAuth] {content}");
			#endif
		}

		public class RobotDetails {
			public readonly string Blid;
			public readonly string Password;
			public readonly string Sku;
			public readonly string Name;

			internal RobotDetails(string blid, JToken robot) {
				Blid = blid;
				Password = (string) robot.SelectToken("password");
				Sku = (string) robot.SelectToken("sku");
				Name = (string) robot.SelectToken("name");
			}
		}
	}
}
