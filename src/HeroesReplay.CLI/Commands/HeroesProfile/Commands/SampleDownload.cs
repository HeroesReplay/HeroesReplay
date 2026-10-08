using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;

namespace HeroesReplay.CLI.Commands.HeroesProfile.Commands;

/// <summary>A listed replay that was not downloaded: the HTTP status Heroes Profile answered (null when it did not answer), and why.</summary>
public sealed record SampleSkip(int ReplayId, int? Status, string Reason);

/// <summary>What one <c>heroesprofile sample</c> run put in the folder, and what it skipped.</summary>
public sealed record SampleOutcome(
    IReadOnlyList<string> Downloaded,
    IReadOnlyList<string> AlreadyThere,
    IReadOnlyList<SampleSkip> Skipped
)
{
    /// <summary>Replays of the sample in the folder now: downloaded, or there before.</summary>
    public int InFolder => Downloaded.Count + AlreadyThere.Count;

    /// <summary>1 only when the folder got none of the listed replays.</summary>
    public int ExitCode => InFolder > 0 ? 0 : 1;

    /// <summary>The summary: the counts, then one line per skipped replay with its reason.</summary>
    public IReadOnlyList<string> Summary(string folder)
    {
        var lines = new List<string>
        {
            $"{Downloaded.Count} downloaded, {AlreadyThere.Count} already in {folder}, {Skipped.Count} skipped.",
        };
        lines.AddRange(
            Skipped.Select(skip => $"Skipped {skip.ReplayId}: {skip.Reason.TrimEnd('.')}.")
        );
        return lines;
    }
}

/// <summary>
/// Downloads listed replays, newest first, until <c>count</c> are in the folder or the listing
/// runs out (#346). Each download goes through the Heroes Profile client and its resilience
/// pipeline (<c>HeroesProfileHttp</c>: retries, and <c>Retry-After</c> on a 429). One that still
/// fails is skipped: its replay id and HTTP status are logged, its partial file is deleted, and
/// the next listed replay is tried. Cancelling stops the run.
/// </summary>
public sealed class SampleDownload
{
    private readonly IHeroesProfileService heroesProfile;
    private readonly ILogger logger;
    private readonly TextWriter output;

    public SampleDownload(IHeroesProfileService heroesProfile, ILogger logger, TextWriter output)
    {
        this.heroesProfile =
            heroesProfile ?? throw new ArgumentNullException(nameof(heroesProfile));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <param name="listed">The candidates, in the order to try them (newest first).</param>
    /// <param name="count">How many replays the folder should hold.</param>
    /// <param name="folder">The sample folder. It must exist.</param>
    /// <param name="fileName">The cache-style file name of one replay.</param>
    /// <param name="cancellationToken">Stops the run; a cancelled download is not a skip.</param>
    public async Task<SampleOutcome> RunAsync(
        IEnumerable<HeroesProfileReplay> listed,
        int count,
        string folder,
        Func<HeroesProfileReplay, string> fileName,
        CancellationToken cancellationToken
    )
    {
        var downloaded = new List<string>();
        var alreadyThere = new List<string>();
        var skipped = new List<SampleSkip>();
        foreach (HeroesProfileReplay replay in listed)
        {
            if (downloaded.Count + alreadyThere.Count >= count)
            {
                break;
            }

            string name = fileName(replay);
            string path = Path.Combine(folder, name);
            if (File.Exists(path))
            {
                alreadyThere.Add(name);
                output.WriteLine($"Have {name}.");
                continue;
            }

            string partial = path + ".tmp";
            try
            {
                await using (FileStream file = File.Create(partial))
                {
                    await heroesProfile
                        .DownloadReplayAsync(replay.Id, file, cancellationToken)
                        .ConfigureAwait(false);
                }

                File.Move(partial, path, overwrite: true);
            }
            catch (Exception e)
            {
                DeletePartial(partial);
                if (e is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                skipped.Add(Skip(replay.Id, e));
                continue;
            }

            downloaded.Add(name);
            output.WriteLine($"Downloaded {name} ({replay.GameVersion}).");
        }

        return new SampleOutcome(downloaded, alreadyThere, skipped);
    }

    private SampleSkip Skip(int replayId, Exception error)
    {
        if (error is ApiException { ResponseStatusCode: > 0 } answered)
        {
            logger.LogWarning(
                "Skipped replay {ReplayId}: Heroes Profile answered its download with HTTP {Status}. Trying the next listed replay.",
                replayId,
                answered.ResponseStatusCode
            );
            return new SampleSkip(
                replayId,
                answered.ResponseStatusCode,
                $"HTTP {answered.ResponseStatusCode}"
            );
        }

        string reason = $"{error.GetType().Name}: {error.Message}";
        logger.LogWarning(
            error,
            "Skipped replay {ReplayId}: its download failed ({Reason}). Trying the next listed replay.",
            replayId,
            reason
        );
        return new SampleSkip(replayId, null, reason);
    }

    private void DeletePartial(string partial)
    {
        try
        {
            File.Delete(partial);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not remove the partial download {Path}.", partial);
        }
    }
}
