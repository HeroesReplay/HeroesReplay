
using TwitchLib.Client.Events;

namespace HeroesReplay.Core.Services.Twitch.RewardHandlers
{
    public interface IOnMessageHandler
    {
        void Handle(OnMessageReceivedArgs args);
    }
}
