
using TwitchLib.Client.Events;

namespace HeroesReplay.Service.Twitch.Core.ChatMessages
{
    public interface IOnMessageHandler
    {
        void Handle(OnMessageReceivedArgs args);
    }
}
