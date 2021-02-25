using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.RewardHandlers
{
    public interface IOnRewardHandler
    {
        void Handle(OnRewardRedeemedArgs args);
    }
}
