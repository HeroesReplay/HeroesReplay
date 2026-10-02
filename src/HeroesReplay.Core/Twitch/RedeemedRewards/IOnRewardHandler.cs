using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Twitch.RedeemedRewards;

public interface IOnRewardHandler
{
    void Handle(OnRewardRedeemedArgs args);
}
