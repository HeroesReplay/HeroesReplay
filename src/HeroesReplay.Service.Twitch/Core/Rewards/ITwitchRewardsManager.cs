using System.Threading.Tasks;

namespace HeroesReplay.Service.Twitch.Core.Rewards
{
    public interface ITwitchRewardsManager
    {
        Task CreateOrUpdateAsync();
        Task GenerateAsync();
    }
}
