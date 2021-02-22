
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.RewardHandlers
{
    public interface IOnRewardRedeemedHandler
    {
        void Handle(OnRewardRedeemedArgs args);
    }
}
