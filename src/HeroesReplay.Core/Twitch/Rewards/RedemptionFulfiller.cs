using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Twitch.Rewards;

/// <summary>
/// Marks a viewer's redemption FULFILLED on Twitch once the spectator verified the requested
/// match (#165). The spectator never calls Helix: it appends the verified session to
/// <c>Data\redemption-dispositions.txt</c>, and this loop in <c>twitch connect</c> sends it.
/// A redemption that was sent, or that Twitch refused for good, is written to
/// <c>Data\redemption-fulfilled.txt</c> and is not sent again. A network error is retried on the
/// next pass. A session that was not verified writes nothing, so its redemption stays UNFULFILLED.
/// </summary>
public sealed class RedemptionFulfiller
{
    public const string SentFileName = "redemption-fulfilled.txt";

    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly ILogger<RedemptionFulfiller> logger;
    private readonly AppSettings settings;
    private readonly IRedemptionStatusClient twitch;
    private bool warnedNotConfigured;

    public RedemptionFulfiller(
        ILogger<RedemptionFulfiller> logger,
        AppSettings settings,
        IRedemptionStatusClient twitch
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.twitch = twitch ?? throw new ArgumentNullException(nameof(twitch));
    }

    /// <summary>
    /// Redemptions are only changed where channel-point redemptions are handled, and never in a
    /// Twitch dry run (#146).
    /// </summary>
    public static bool Enabled(TwitchSettings twitch) =>
        twitch is not null && (twitch.EnablePubSub || twitch.EnableRequests) && !twitch.DryRunMode;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!Enabled(settings.Twitch))
        {
            logger.LogInformation(
                "Redemptions are not marked fulfilled on Twitch (requests off, or Twitch:DryRunMode)."
            );
            return;
        }

        logger.LogInformation(
            "Marking verified requests FULFILLED on Twitch from {Path}.",
            DispositionsPath()
        );
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SendPendingAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(e, "Could not read or record redemption dispositions.");
            }

            try
            {
                await Task.Delay(Interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass. Returns how many redemptions Twitch now shows FULFILLED.</summary>
    public async Task<int> SendPendingAsync(CancellationToken cancellationToken)
    {
        string sentPath = SentPath();
        if (sentPath == null)
        {
            return 0;
        }

        HashSet<Guid> sent = ReadSent(sentPath);
        int fulfilled = 0;
        foreach (
            RedemptionDispositionLine line in RedemptionDispositionLog.Read(DispositionsPath())
        )
        {
            if (line.End != RedemptionEnd.Fulfill || !sent.Add(line.RedemptionId))
            {
                continue;
            }

            if (line.RewardId == Guid.Empty || string.IsNullOrWhiteSpace(line.BroadcasterId))
            {
                logger.LogWarning(
                    "Redemption {RedemptionId} for replay {ReplayId} was verified, but its reward was not recorded. Mark it fulfilled in the Twitch reward queue.",
                    line.RedemptionId,
                    line.ReplayId
                );
                MarkSent(sentPath, line, "unknown-reward");
                continue;
            }

            RedemptionUpdate result = await twitch
                .UpdateAsync(
                    line.BroadcasterId,
                    line.RewardId,
                    line.RedemptionId,
                    RewardRedemptionStatus.Fulfilled,
                    cancellationToken
                )
                .ConfigureAwait(false);
            switch (result)
            {
                case RedemptionUpdate.Updated:
                    fulfilled++;
                    MarkSent(sentPath, line, "fulfilled");
                    logger.LogInformation(
                        "Redemption {RedemptionId} for replay {ReplayId} is FULFILLED on Twitch.",
                        line.RedemptionId,
                        line.ReplayId
                    );
                    break;
                case RedemptionUpdate.Refused:
                    MarkSent(sentPath, line, "refused");
                    logger.LogWarning(
                        "Twitch refused to fulfil redemption {RedemptionId} for replay {ReplayId}. It is not sent again.",
                        line.RedemptionId,
                        line.ReplayId
                    );
                    break;
                case RedemptionUpdate.NotConfigured:
                    sent.Remove(line.RedemptionId);
                    if (!warnedNotConfigured)
                    {
                        warnedNotConfigured = true;
                        logger.LogWarning(
                            "Twitch:ClientId or Twitch:AccessToken is missing. Verified redemptions wait until it is set."
                        );
                    }

                    break;
                default:
                    // Not recorded, so the next pass sends it again.
                    sent.Remove(line.RedemptionId);
                    break;
            }
        }

        return fulfilled;
    }

    private string DispositionsPath() =>
        settings.Location?.DataDirectory == null
            ? null
            : Path.Combine(settings.Location.DataDirectory, RedemptionDispositionLog.FileName);

    private string SentPath() =>
        settings.Location?.DataDirectory == null
            ? null
            : Path.Combine(settings.Location.DataDirectory, SentFileName);

    private static HashSet<Guid> ReadSent(string path)
    {
        var sent = new HashSet<Guid>();
        if (!File.Exists(path))
        {
            return sent;
        }

        foreach (string line in File.ReadAllLines(path))
        {
            string first = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)
                is { Length: > 0 } parts
                ? parts[0]
                : null;
            if (Guid.TryParse(first, out Guid id))
            {
                sent.Add(id);
            }
        }

        return sent;
    }

    private static void MarkSent(string path, RedemptionDispositionLine line, string result)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllText(
            path,
            line.RedemptionId.ToString("D")
                + " "
                + result
                + " "
                + line.ReplayId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + Environment.NewLine
        );
    }
}
