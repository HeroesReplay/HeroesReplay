using TwitchLib.Client.Events;

namespace HeroesReplay.Core.Twitch.ChatMessages;

public interface IOnMessageHandler
{
    void Handle(OnMessageReceivedArgs args);
}
