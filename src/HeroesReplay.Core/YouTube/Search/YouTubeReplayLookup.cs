using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Replays;

namespace HeroesReplay.Core.YouTube.Search;

/// <summary>
/// A replay is already on YouTube when <c>Data\youtube-replay-ids.txt</c> or an upload
/// receipt names it. This reads local files only. The uploader process keeps the catalog
/// current: it adds each replay it inserts, and its library pass adds every replay id it
/// finds on the channel.
/// </summary>
public sealed class YouTubeReplayLookup : IYouTubeReplayLookup
{
    private readonly AppSettings settings;

    public YouTubeReplayLookup(AppSettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public Task<bool> AlreadyUploadedAsync(LoadedReplay replay, CancellationToken cancellationToken)
    {
        int? replayId = replay?.HeroesProfileReplay?.Id ?? replay?.ReplayId;
        if (replayId is not > 0)
        {
            return Task.FromResult(false);
        }

        string catalog = YouTubeReplayCatalog.PathFor(settings.Location?.DataDirectory);
        if (YouTubeReplayCatalog.Contains(catalog, replayId.Value))
        {
            return Task.FromResult(true);
        }

        if (HasUploadReceipt(replayId.Value))
        {
            YouTubeReplayCatalog.Remember(catalog, replayId.Value);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private bool HasUploadReceipt(int replayId)
    {
        string uploadedName = settings.YouTube?.EntryFileNameUploaded;
        if (
            string.IsNullOrWhiteSpace(uploadedName)
            || string.IsNullOrWhiteSpace(settings.ContextsDirectory)
        )
        {
            return false;
        }

        string receipt = Path.Combine(
            settings.ContextsDirectory,
            replayId.ToString(),
            uploadedName
        );
        return File.Exists(receipt);
    }
}
