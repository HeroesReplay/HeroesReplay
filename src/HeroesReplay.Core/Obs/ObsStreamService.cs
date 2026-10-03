using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// What the agent tools may say about the OBS stream service: its type, the named service of a
/// <c>rtmp_common</c> type (<c>Twitch</c>), and whether a key is set. GetStreamServiceSettings
/// also returns the stream key, the server, and any username or password.
/// <see cref="Summarize"/> reads only those three, and the response goes no further.
/// </summary>
public sealed record ObsStreamService(string Type, bool KeySet, string Service = null)
{
    public static ObsStreamService Summarize(JObject response)
    {
        string type =
            response?["streamServiceType"]?.Type == JTokenType.String
                ? (string)response["streamServiceType"]
                : null;
        var settings = response?["streamServiceSettings"] as JObject;
        JToken key = settings?["key"];
        bool keySet = key?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)key);
        // A custom (rtmp_custom) server is an address and may carry a key; only a named service is kept.
        string service =
            type == "rtmp_common" && settings?["service"]?.Type == JTokenType.String
                ? (string)settings["service"]
                : null;
        return new ObsStreamService(type, keySet, service);
    }
}
