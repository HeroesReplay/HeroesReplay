using System.Collections.Generic;
using System.IO;
using System.Threading;
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
    Task DownloadReplayAsync(int replayId, Stream destination, CancellationToken cancellationToken);
    Task EnrichRankAsync(HeroesProfileReplay replay, CancellationToken cancellationToken);
}
