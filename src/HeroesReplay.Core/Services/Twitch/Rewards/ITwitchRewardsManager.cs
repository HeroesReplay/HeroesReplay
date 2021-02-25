using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Twitch.Rewards
{
    public interface ITwitchRewardsManager
    {
        Task CreateOrUpdateAsync();
        Task GenerateAsync();
    }
}
