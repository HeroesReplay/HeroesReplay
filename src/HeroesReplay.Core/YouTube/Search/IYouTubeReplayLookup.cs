using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Replays;

namespace HeroesReplay.Core.YouTube.Search;

public sealed class YouTubeVideoText
{
    public string Title { get; init; }
    public string Description { get; init; }
}

public interface IYouTubeVideoSearch
{
    Task<IReadOnlyList<YouTubeVideoText>> SearchAsync(
        int replayId,
        CancellationToken cancellationToken
    );
}

public interface IYouTubeReplayLookup
{
    Task<bool> AlreadyUploadedAsync(LoadedReplay replay, CancellationToken cancellationToken);
}
