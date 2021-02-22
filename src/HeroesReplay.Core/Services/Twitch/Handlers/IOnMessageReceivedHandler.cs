using System.Linq;

using TwitchLib.Client.Events;

namespace HeroesReplay.Core.Services.Twitch.RewardHandlers
{
    public interface IOnMessageReceivedHandler
    {
        void Handle(OnMessageReceivedArgs args);
    }
}
