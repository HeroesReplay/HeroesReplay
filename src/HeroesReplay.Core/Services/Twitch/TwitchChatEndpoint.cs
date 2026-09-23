namespace HeroesReplay.Core.Services.Twitch;

public static class TwitchChatEndpoint
{
    /// <summary>
    /// Insecure ws://irc-ws.chat.twitch.tv:80 was shut down in August 2025.
    /// </summary>
    public const string SecureWebSocket = "wss://irc-ws.chat.twitch.tv:443";
}
