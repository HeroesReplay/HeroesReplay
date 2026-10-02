using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Replays;

namespace HeroesReplay.Core.YouTube.Search;

public interface IYouTubeReplayLookup
{
    Task<bool> AlreadyUploadedAsync(LoadedReplay replay, CancellationToken cancellationToken);
}
