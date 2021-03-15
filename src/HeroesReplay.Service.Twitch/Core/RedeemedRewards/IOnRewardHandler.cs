using TwitchLib.PubSub.Events;

namespace HeroesReplay.Service.Twitch.Core.RedeemedRewards
{
    public interface IOnRewardHandler
    {
        void Handle(OnRewardRedeemedArgs args);
    }
}
