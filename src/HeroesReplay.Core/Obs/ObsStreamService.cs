using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// What the agent tools may say about the OBS stream service: its type and whether a key is
/// set. GetStreamServiceSettings also returns the stream key, the server, and any username
/// or password. <see cref="Summarize"/> reads only the type and the key's presence, and the
/// response goes no further.
/// </summary>
public sealed record ObsStreamService(string Type, bool KeySet)
{
    public static ObsStreamService Summarize(JObject response)
    {
        string type =
            response?["streamServiceType"]?.Type == JTokenType.String
                ? (string)response["streamServiceType"]
                : null;
        JToken key = (response?["streamServiceSettings"] as JObject)?["key"];
        bool keySet = key?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)key);
        return new ObsStreamService(type, keySet);
    }
}
