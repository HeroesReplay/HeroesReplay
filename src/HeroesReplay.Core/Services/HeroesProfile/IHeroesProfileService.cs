using System.Collections.Generic;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.HeroesProfile;

public interface IHeroesProfileService
{
    Task<int> GetMaxReplayIdAsync();
    Task<IEnumerable<HeroesProfileReplay>> GetReplaysByFilters(
        GameType? gameType = null,
        GameRank? gameRank = null,
        string gameMap = null
    );
    Task<HeroesProfileReplay> GetReplayByIdAsync(int replayId);
    Task<IEnumerable<HeroesProfileReplay>> GetReplaysByMinId(int minId);
}
