using TwitchLib.Client.Models;

namespace HeroesReplay.Core.Twitch.ChatMessages;

public interface IMessageHandler
{
    bool CanHandle(ChatMessage chatMessage);
    void Execute(ChatMessage chatMessage);
}
