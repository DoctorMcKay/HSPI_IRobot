using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace IRobotLANClient {
	internal class JsonCompare {
		internal static IEnumerable<string> Compare(JObject obj1, JObject obj2, string path = "") {
			List<string> output = new List<string>();
			foreach (JProperty prop in obj1.Properties()) {
				obj2.TryGetValue(prop.Name, out JToken token2);
				if (token2 == null) {
					output.Add($"{path}{prop.Path}: \"{prop.Value}\" -> UNSET");
					continue;
				}

				if (prop.Value.Type == JTokenType.Array && token2.Type == JTokenType.Array) {
					JArray arr1 = (JArray) prop.Value;
					JArray arr2 = (JArray) token2;
					if (arr1.Count != arr2.Count) {
						output.Add($"{path}{prop.Path}: Length {arr1.Count} -> Length {arr2.Count}");
					}
					
					continue;
				}

				if (prop.Value.Type == JTokenType.Object && token2.Type == JTokenType.Object) {
					output.AddRange(
						Compare(
							JObject.Parse(prop.Value.ToString()),
							JObject.Parse(token2.ToString()),
							(!string.IsNullOrEmpty(path) ? path : "") + prop.Path + "."
						)
					);
					continue;
				}

				if (prop.Value.Type != token2.Type || prop.Value.ToString() != token2.ToString()) {
					output.Add($"{path}{prop.Path}: \"{prop.Value}\" -> \"{token2.Value<string>()}\"");
				}
			}

			return output;
		}
	}
}
