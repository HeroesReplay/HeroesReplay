using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Twitch.Rewards;

/// <summary>
/// Sends the redemption dispositions to Twitch from <c>twitch connect</c>. The spectator and the
/// download role never call Helix: they append to <c>Data\redemption-dispositions.txt</c>, and
/// this loop sends each line. A <see cref="RedemptionEnd.Fulfill"/> line (the spectator verified
/// the requested match, #165) marks the redemption FULFILLED. A <see cref="RedemptionEnd.Cancel"/>
/// line (the download role found the requested replay can never be downloaded, #351) cancels it
/// through <see cref="IRedemptionCanceller"/>, which returns the viewer's points, unless the same
/// redemption also has a Fulfill line. A redemption that was sent, or that Twitch refused for
/// good, is written to <c>Data\redemption-fulfilled.txt</c> and is not sent again. A network
/// error is retried on the next pass. A session that was not verified writes nothing, so its
/// redemption stays UNFULFILLED.
/// </summary>
public sealed class RedemptionFulfiller
{
    public const string SentFileName = "redemption-fulfilled.txt";

    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly ILogger<RedemptionFulfiller> logger;
    private readonly AppSettings settings;
    private readonly IRedemptionStatusClient twitch;
    private readonly IRedemptionCanceller canceller;
    private bool warnedNotConfigured;

    public RedemptionFulfiller(
        ILogger<RedemptionFulfiller> logger,
        AppSettings settings,
        IRedemptionStatusClient twitch,
        IRedemptionCanceller canceller
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.twitch = twitch ?? throw new ArgumentNullException(nameof(twitch));
        this.canceller = canceller ?? throw new ArgumentNullException(nameof(canceller));
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
                "Redemptions are not marked fulfilled or cancelled on Twitch (requests off, or Twitch:DryRunMode)."
            );
            return;
        }

        logger.LogInformation(
            "Marking verified requests FULFILLED, and requests whose replay cannot be downloaded CANCELED, on Twitch from {Path}.",
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

    /// <summary>One pass. Returns how many redemptions Twitch now shows FULFILLED or CANCELED.</summary>
    public async Task<int> SendPendingAsync(CancellationToken cancellationToken)
    {
        string sentPath = SentPath();
        if (sentPath == null)
        {
            return 0;
        }

        IReadOnlyList<RedemptionDispositionLine> lines = RedemptionDispositionLog.Read(
            DispositionsPath()
        );
        var verified = new HashSet<Guid>();
        foreach (RedemptionDispositionLine line in lines)
        {
            if (line.End == RedemptionEnd.Fulfill)
            {
                verified.Add(line.RedemptionId);
            }
        }

        HashSet<Guid> sent = ReadSent(sentPath);
        int updated = 0;
        foreach (RedemptionDispositionLine line in lines)
        {
            bool fulfil = line.End == RedemptionEnd.Fulfill;
            bool cancel = line.End == RedemptionEnd.Cancel;
            // A played and verified match is never refunded, whatever else was recorded.
            if ((!fulfil && !cancel) || (cancel && verified.Contains(line.RedemptionId)))
            {
                continue;
            }

            if (!sent.Add(line.RedemptionId))
            {
                continue;
            }

            if (line.RewardId == Guid.Empty || string.IsNullOrWhiteSpace(line.BroadcasterId))
            {
                if (fulfil)
                {
                    logger.LogWarning(
                        "Redemption {RedemptionId} for replay {ReplayId} was verified, but its reward was not recorded. Mark it fulfilled in the Twitch reward queue.",
                        line.RedemptionId,
                        line.ReplayId
                    );
                }
                else
                {
                    logger.LogWarning(
                        "Redemption {RedemptionId} for replay {ReplayId} cannot be played, but its reward was not recorded. Reject it in the Twitch reward queue to return the points.",
                        line.RedemptionId,
                        line.ReplayId
                    );
                }

                MarkSent(sentPath, line, "unknown-reward");
                continue;
            }

            RedemptionUpdate result = fulfil
                ? await twitch
                    .UpdateAsync(
                        line.BroadcasterId,
                        line.RewardId,
                        line.RedemptionId,
                        RewardRedemptionStatus.Fulfilled,
                        cancellationToken
                    )
                    .ConfigureAwait(false)
                : await canceller
                    .CancelAsync(
                        line.BroadcasterId,
                        line.RewardId,
                        line.RedemptionId,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            switch (result)
            {
                case RedemptionUpdate.Updated:
                    updated++;
                    MarkSent(sentPath, line, fulfil ? "fulfilled" : "canceled");
                    if (fulfil)
                    {
                        logger.LogInformation(
                            "Redemption {RedemptionId} for replay {ReplayId} is FULFILLED on Twitch.",
                            line.RedemptionId,
                            line.ReplayId
                        );
                    }
                    else
                    {
                        logger.LogInformation(
                            "Redemption {RedemptionId} for replay {ReplayId} is CANCELED on Twitch. The viewer's points were returned.",
                            line.RedemptionId,
                            line.ReplayId
                        );
                    }

                    break;
                case RedemptionUpdate.Refused:
                    MarkSent(sentPath, line, "refused");
                    if (fulfil)
                    {
                        logger.LogWarning(
                            "Twitch refused to fulfil redemption {RedemptionId} for replay {ReplayId}. It is not sent again.",
                            line.RedemptionId,
                            line.ReplayId
                        );
                    }
                    else
                    {
                        logger.LogWarning(
                            "Twitch refused to cancel redemption {RedemptionId} for replay {ReplayId}. It is not sent again. Reject it in the Twitch reward queue to return the points.",
                            line.RedemptionId,
                            line.ReplayId
                        );
                    }

                    break;
                case RedemptionUpdate.NotConfigured:
                    sent.Remove(line.RedemptionId);
                    if (!warnedNotConfigured)
                    {
                        warnedNotConfigured = true;
                        logger.LogWarning(
                            "Twitch:ClientId or Twitch:AccessToken is missing. Redemptions wait until it is set."
                        );
                    }

                    break;
                default:
                    // Not recorded, so the next pass sends it again.
                    sent.Remove(line.RedemptionId);
                    break;
            }
        }

        return updated;
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
