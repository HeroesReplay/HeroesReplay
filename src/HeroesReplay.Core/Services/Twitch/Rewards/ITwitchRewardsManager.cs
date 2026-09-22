using System.Collections.Generic;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Twitch.Rewards;

public interface ITwitchRewardsManager
{
    Task CreateOrUpdateAsync();
    Task GenerateAsync();
    Task<IReadOnlyList<string>> ListRemoteTitlesAsync();
    Task<UnrankedDraftRewardRemoval> DeleteUnrankedDraftRewardsAsync();
}
