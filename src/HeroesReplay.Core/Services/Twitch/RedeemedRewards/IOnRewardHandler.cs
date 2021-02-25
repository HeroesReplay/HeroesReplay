using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.RedeemedRewards
{
    public interface IOnRewardHandler
    {
        void Handle(OnRewardRedeemedArgs args);
    }
}
