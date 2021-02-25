
using TwitchLib.Client.Events;

namespace HeroesReplay.Core.Services.Twitch.ChatMessages
{
    public interface IOnMessageHandler
    {
        void Handle(OnMessageReceivedArgs args);
    }
}
