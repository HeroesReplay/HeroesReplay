using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Exceptions;
using TwitchLib.Api.Helix.Models.Predictions;
using TwitchLib.Api.Helix.Models.Predictions.CreatePrediction;
using TwitchLib.Api.Interfaces;
using CreateOutcome = TwitchLib.Api.Helix.Models.Predictions.CreatePrediction.Outcome;

namespace HeroesReplay.Core.Services.Twitch;

public class TwitchMatchPredictionService : IMatchPredictionService
{
    private readonly ILogger<TwitchMatchPredictionService> logger;
    private readonly AppSettings settings;
    private readonly ITwitchAPI api;
    private readonly PredictionReportWriter reports;
    private readonly PredictionLedger ledger;
    private string cachedChannelId;
    private int? currentReplayId;

    public TwitchMatchPredictionService(
        ILogger<TwitchMatchPredictionService> logger,
        AppSettings settings,
        ITwitchAPI api,
        PredictionReportWriter reports
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.reports = reports ?? throw new ArgumentNullException(nameof(reports));
        ledger = PredictionLedger.Load(PredictionLedger.PathFor(settings.Location?.DataDirectory));
    }

    public Task StartAsync(LoadedReplay replay, CancellationToken cancellationToken) =>
        OpenAsync(
            replay?.ReplayId ?? 0,
            EnglishMapNames.Prefer(null, replay?.Replay?.Map, replay?.Replay?.MapAlternativeName),
            cancellationToken
        );

    public async Task<bool> OpenAsync(int replayId, string map, CancellationToken cancellationToken)
    {
        if (!settings.Twitch.EnablePredictions || settings.Capture.Method == CaptureMethod.None)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        string title = MatchPrediction.TitleForMap(map);
        reports.TryWriteCurrent(title);
        if (settings.Twitch.DryRunMode)
        {
            logger.LogInformation(
                "Dry-run prediction: {Title} ({Window}s) Blue vs Red.",
                title,
                MatchPrediction.WindowSeconds(settings.Twitch.PredictionWindow)
            );
            return true;
        }

        Prediction[] remote;
        try
        {
            remote = await FetchChannelAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not read Twitch predictions before opening one.");
            return false;
        }

        Prediction channel = ActiveOrLocked(remote);
        if (channel != null)
        {
            PredictionLedgerEntry owned = ledger.FindByPredictionId(channel.Id);
            if (
                owned != null
                && owned.ReplayId == replayId
                && owned.State is PredictionLedgerState.Open or PredictionLedgerState.Pending
            )
            {
                RememberChannel(owned, channel);
                logger.LogInformation(
                    "Adopted prediction {PredictionId} for replay {ReplayId} attempt {Attempt} ({Status}). Map title is not identity.",
                    channel.Id,
                    owned.ReplayId,
                    owned.Attempt,
                    channel.Status
                );
                return true;
            }

            if (owned == null)
            {
                logger.LogWarning(
                    "Twitch prediction {PredictionId} ({Title}, {Status}) is not in the ledger. Not adopting it.",
                    channel.Id,
                    channel.Title,
                    channel.Status
                );
            }
            else
            {
                logger.LogWarning(
                    "Twitch prediction {PredictionId} belongs to replay {ReplayId} attempt {Attempt}, not replay {Requested}.",
                    channel.Id,
                    owned.ReplayId,
                    owned.Attempt,
                    replayId
                );
            }

            return false;
        }

        string channelId = await GetChannelIdAsync().ConfigureAwait(false);
        var request = new CreatePredictionRequest
        {
            BroadcasterId = channelId,
            Title = title,
            PredictionWindowSeconds = MatchPrediction.WindowSeconds(
                settings.Twitch.PredictionWindow
            ),
            Outcomes = new[]
            {
                new CreateOutcome { Title = MatchPrediction.Blue },
                new CreateOutcome { Title = MatchPrediction.Red },
            },
        };

        CreatePredictionResponse created;
        try
        {
            created = await api
                .Helix.Predictions.CreatePredictionAsync(request)
                .ConfigureAwait(false);
        }
        catch (BadRequestException)
        {
            logger.LogWarning(
                "Could not open \"{Title}\". Twitch already has a prediction on this channel.",
                request.Title
            );
            return false;
        }

        Prediction prediction = First(created?.Data);
        if (prediction == null || string.IsNullOrWhiteSpace(prediction.Id))
        {
            logger.LogWarning("Helix CreatePrediction returned no prediction.");
            return false;
        }

        int attempt = ledger.NextAttempt(replayId);
        var entry = new PredictionLedgerEntry
        {
            SessionKey = PredictionSessionKey.Format(replayId, attempt),
            ReplayId = replayId,
            Attempt = attempt,
            PredictionId = prediction.Id,
            BlueOutcomeId = FindOutcomeId(prediction.Outcomes, MatchPrediction.Blue),
            RedOutcomeId = FindOutcomeId(prediction.Outcomes, MatchPrediction.Red),
            BroadcasterId = string.IsNullOrWhiteSpace(prediction.BroadcasterId)
                ? channelId
                : prediction.BroadcasterId,
            Title = title,
            Map = map,
            CreatedAt = DateTimeOffset.UtcNow,
            State = PredictionLedgerState.Open,
            Intent = PredictionIntent.None,
        };
        ledger.Upsert(entry);
        currentReplayId = replayId;
        logger.LogInformation(
            "Opened Blue/Red prediction {PredictionId} for replay {ReplayId} attempt {Attempt} ({Title}, {Window}s).",
            prediction.Id,
            replayId,
            attempt,
            request.Title,
            request.PredictionWindowSeconds
        );
        return true;
    }

    public async Task TestAsync(int? winningTeam, CancellationToken cancellationToken)
    {
        var dummy = new LoadedReplay { Replay = new Replay { Map = "Test" } };
        bool savedDry = settings.Twitch.DryRunMode;
        CaptureMethod savedCapture =
            settings.Capture != null ? settings.Capture.Method : CaptureMethod.BitBlt;
        TimeSpan savedWindow = settings.Twitch.PredictionWindow;
        try
        {
            settings.Twitch.DryRunMode = false;
            settings.Twitch.EnablePredictions = true;
            if (settings.Capture == null)
            {
                settings.Capture = new CaptureSettings();
            }

            settings.Capture.Method = CaptureMethod.BitBlt;
            settings.Twitch.PredictionWindow = TimeSpan.FromSeconds(30);
            await OpenAsync(dummy.ReplayId ?? 0, dummy.Replay.Map, cancellationToken)
                .ConfigureAwait(false);
            await ResolveTeamAsync(winningTeam, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            settings.Twitch.DryRunMode = savedDry;
            if (settings.Capture != null)
            {
                settings.Capture.Method = savedCapture;
            }

            settings.Twitch.PredictionWindow = savedWindow;
        }
    }

    public Task ResolveAsync(LoadedReplay replay, CancellationToken cancellationToken)
    {
        int? team = replay?.Replay == null ? null : WinningTeam(replay.Replay);
        if (replay?.ReplayId is int replayId)
        {
            return ResolveReplayAsync(replayId, team, cancellationToken);
        }

        return ResolveTeamAsync(team, cancellationToken);
    }

    public Task ResolveTeamAsync(int? winningTeam, CancellationToken cancellationToken)
    {
        if (currentReplayId is int replayId)
        {
            return ResolveReplayAsync(replayId, winningTeam, cancellationToken);
        }

        PredictionLedgerEntry entry = ledger.FindLatestUnsettled();
        if (entry == null)
        {
            if (settings.Twitch.DryRunMode)
            {
                LogDrySettle(winningTeam);
            }

            return Task.CompletedTask;
        }

        return ResolveReplayAsync(entry.ReplayId, winningTeam, cancellationToken);
    }

    public async Task ResolveReplayAsync(
        int replayId,
        int? winningTeam,
        CancellationToken cancellationToken
    )
    {
        if (!LiveHelix())
        {
            if (settings.Twitch.DryRunMode)
            {
                LogDrySettle(winningTeam);
            }

            return;
        }

        PredictionLedgerEntry entry = ledger.FindUnsettled(replayId);
        if (entry == null)
        {
            return;
        }

        if (entry.State == PredictionLedgerState.Pending && entry.Intent != PredictionIntent.None)
        {
            await TryExecuteAsync(entry, cancellationToken).ConfigureAwait(false);
            return;
        }

        entry.Intent = winningTeam.HasValue ? PredictionIntent.Resolve : PredictionIntent.Cancel;
        entry.WinningTeam = winningTeam;
        entry.State = PredictionLedgerState.Pending;
        entry.FailureCount = 0;
        entry.NextAttemptAt = null;
        ledger.Upsert(entry);
        currentReplayId = replayId;
        await TryExecuteAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PredictionReconcileResult> ReconcileAsync(CancellationToken cancellationToken)
    {
        if (!LiveHelix())
        {
            return PredictionReconcileResult.Empty;
        }

        Prediction[] remote;
        try
        {
            remote = await FetchChannelAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not reconcile Twitch predictions. The ledger was kept.");
            return PredictionReconcileResult.Empty;
        }

        PredictionReconcileResult result = ClassifyRemote(remote);
        ReportReconcile(result, remote);
        foreach (PredictionLedgerEntry entry in ledger.Copy())
        {
            if (
                entry == null
                || entry.State is PredictionLedgerState.Settled or PredictionLedgerState.Terminal
            )
            {
                continue;
            }

            Prediction match = FindPrediction(remote, entry.PredictionId);
            if (match == null)
            {
                continue;
            }

            PredictionRemoteState remoteState = MapStatus(match.Status);
            PredictionSettlementPlan decision = PredictionSettlementPolicy.Plan(
                entry.State,
                entry.Intent,
                remoteState,
                SettlementFailure.None,
                backoffElapsed: true
            );
            if (
                decision.ClearIntent
                && !decision.Terminal
                && remoteState is PredictionRemoteState.Resolved or PredictionRemoteState.Canceled
            )
            {
                MarkSettled(entry, remoteState);
                if (remoteState == PredictionRemoteState.Resolved)
                {
                    reports.TryWrite(match);
                }
            }
        }

        result = ClassifyRemote(remote);
        if (result.Resume != null)
        {
            currentReplayId = result.Resume.ReplayId;
        }

        return result;
    }

    public async Task RetryPendingAsync(CancellationToken cancellationToken)
    {
        if (!LiveHelix())
        {
            return;
        }

        foreach (PredictionLedgerEntry entry in ledger.Copy())
        {
            if (
                entry == null
                || entry.State != PredictionLedgerState.Pending
                || entry.Intent == PredictionIntent.None
            )
            {
                continue;
            }

            await TryExecuteAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    public static int? WinningTeam(Replay replay)
    {
        if (replay?.Players == null)
        {
            return null;
        }

        int? team = null;
        foreach (Player player in replay.Players)
        {
            if (player == null || !player.IsWinner)
            {
                continue;
            }

            if (team == null)
            {
                team = player.Team;
            }
            else if (team.Value != player.Team)
            {
                return null;
            }
        }

        return team;
    }

    private async Task TryExecuteAsync(
        PredictionLedgerEntry entry,
        CancellationToken cancellationToken
    )
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.PredictionId))
        {
            return;
        }

        if (entry.State is PredictionLedgerState.Settled or PredictionLedgerState.Terminal)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        bool backoffElapsed = PredictionSettlementPolicy.BackoffElapsed(
            DateTimeOffset.UtcNow,
            entry.NextAttemptAt
        );
        PredictionRemoteState remote = PredictionRemoteState.Unknown;
        try
        {
            Prediction fetched = await FetchOneAsync(entry, cancellationToken)
                .ConfigureAwait(false);
            remote = fetched == null ? PredictionRemoteState.Missing : MapStatus(fetched.Status);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ApplyFailure(entry, ex, PredictionRemoteState.Unknown, backoffElapsed);
            return;
        }

        PredictionSettlementPlan plan = PredictionSettlementPolicy.Plan(
            entry.State,
            entry.Intent,
            remote,
            SettlementFailure.None,
            backoffElapsed
        );
        if (!plan.CallsTwitch)
        {
            ApplyIdle(entry, plan, remote, "remote " + remote);
            return;
        }

        try
        {
            string channelId = entry.BroadcasterId;
            if (string.IsNullOrWhiteSpace(channelId))
            {
                channelId = await GetChannelIdAsync().ConfigureAwait(false);
                entry.BroadcasterId = channelId;
            }

            if (plan.ShouldCancel)
            {
                var ended = await api
                    .Helix.Predictions.EndPredictionAsync(
                        channelId,
                        entry.PredictionId,
                        PredictionEndStatus.CANCELED
                    )
                    .ConfigureAwait(false);
                await ConfirmAsync(entry, ended?.Data, cancellationToken).ConfigureAwait(false);
                return;
            }

            string winningId = entry.WinningTeam == 0 ? entry.BlueOutcomeId : entry.RedOutcomeId;
            if (entry.WinningTeam == null || string.IsNullOrWhiteSpace(winningId))
            {
                MarkTerminal(entry, "missing outcome id for team " + entry.WinningTeam);
                return;
            }

            var resolved = await api
                .Helix.Predictions.EndPredictionAsync(
                    channelId,
                    entry.PredictionId,
                    PredictionEndStatus.RESOLVED,
                    winningId
                )
                .ConfigureAwait(false);
            await ConfirmAsync(entry, resolved?.Data, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PredictionRemoteState after = PredictionRemoteState.Unknown;
            if (PredictionSettlementPolicy.Classify(ex) == SettlementFailure.Terminal)
            {
                try
                {
                    Prediction again = await FetchOneAsync(entry, cancellationToken)
                        .ConfigureAwait(false);
                    if (again != null)
                    {
                        after = MapStatus(again.Status);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception read)
                {
                    logger.LogWarning(
                        read,
                        "Could not re-read prediction {PredictionId} after a settlement error.",
                        entry.PredictionId
                    );
                }
            }

            ApplyFailure(entry, ex, after, backoffElapsed: true);
        }
    }

    private async Task ConfirmAsync(
        PredictionLedgerEntry entry,
        Prediction[] data,
        CancellationToken cancellationToken
    )
    {
        Prediction settled = FirstEnded(data);
        PredictionRemoteState remote =
            settled == null ? PredictionRemoteState.Unknown : MapStatus(settled.Status);
        if (remote is not (PredictionRemoteState.Resolved or PredictionRemoteState.Canceled))
        {
            try
            {
                Prediction again = await FetchOneAsync(entry, cancellationToken)
                    .ConfigureAwait(false);
                if (again != null)
                {
                    settled = again;
                    remote = MapStatus(again.Status);
                }
                else
                {
                    remote = PredictionRemoteState.Missing;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                KeepPending(entry, ex.Message);
                return;
            }
        }

        PredictionSettlementPlan plan = PredictionSettlementPolicy.Plan(
            PredictionLedgerState.Pending,
            entry.Intent,
            remote,
            SettlementFailure.None,
            backoffElapsed: true
        );
        if (
            plan.ClearIntent
            && !plan.Terminal
            && remote is PredictionRemoteState.Resolved or PredictionRemoteState.Canceled
        )
        {
            MarkSettled(entry, remote);
            if (remote == PredictionRemoteState.Resolved)
            {
                reports.TryWrite(settled);
            }

            return;
        }

        KeepPending(entry, "Twitch did not confirm the prediction ended");
    }

    private void ApplyFailure(
        PredictionLedgerEntry entry,
        Exception exception,
        PredictionRemoteState remote,
        bool backoffElapsed
    )
    {
        SettlementFailure failure = PredictionSettlementPolicy.Classify(exception);
        if (remote is PredictionRemoteState.Resolved or PredictionRemoteState.Canceled)
        {
            failure = SettlementFailure.None;
        }

        PredictionSettlementPlan plan = PredictionSettlementPolicy.Plan(
            entry.State,
            entry.Intent,
            remote,
            failure,
            backoffElapsed
        );
        if (
            plan.ClearIntent
            && !plan.Terminal
            && remote is PredictionRemoteState.Resolved or PredictionRemoteState.Canceled
        )
        {
            MarkSettled(entry, remote);
            return;
        }

        if (plan.Terminal)
        {
            MarkTerminal(entry, exception.Message);
            return;
        }

        if (plan.ShouldRetry)
        {
            KeepPending(entry, exception.Message);
        }
    }

    private void ApplyIdle(
        PredictionLedgerEntry entry,
        PredictionSettlementPlan plan,
        PredictionRemoteState remote,
        string reason
    )
    {
        if (
            plan.ClearIntent
            && !plan.Terminal
            && remote is PredictionRemoteState.Resolved or PredictionRemoteState.Canceled
        )
        {
            MarkSettled(entry, remote);
            return;
        }

        if (plan.Terminal)
        {
            MarkTerminal(entry, reason);
            return;
        }

        if (plan.ShouldRetry && remote == PredictionRemoteState.Missing)
        {
            bool elapsed = PredictionSettlementPolicy.BackoffElapsed(
                DateTimeOffset.UtcNow,
                entry.NextAttemptAt
            );
            if (elapsed)
            {
                KeepPending(entry, reason);
            }
        }
    }

    private void KeepPending(PredictionLedgerEntry entry, string reason)
    {
        // Intent stays until Twitch confirms RESOLVED or CANCELED, or the error is terminal.
        entry.State = PredictionLedgerState.Pending;
        entry.FailureCount++;
        entry.NextAttemptAt = PredictionSettlementPolicy.NextRetryAt(
            DateTimeOffset.UtcNow,
            entry.FailureCount
        );
        ledger.Upsert(entry);
        logger.LogWarning(
            "Prediction {PredictionId} for replay {ReplayId} attempt {Attempt} stays pending ({Reason}). Next try at {Next}.",
            entry.PredictionId,
            entry.ReplayId,
            entry.Attempt,
            reason,
            entry.NextAttemptAt
        );
    }

    private void MarkSettled(PredictionLedgerEntry entry, PredictionRemoteState remote)
    {
        entry.State = PredictionLedgerState.Settled;
        entry.Intent = PredictionIntent.None;
        entry.SettledStatus = remote == PredictionRemoteState.Canceled ? "CANCELED" : "RESOLVED";
        entry.NextAttemptAt = null;
        entry.TerminalReason = null;
        ledger.Upsert(entry);
        logger.LogInformation(
            "Prediction {PredictionId} for replay {ReplayId} attempt {Attempt} is {Status}.",
            entry.PredictionId,
            entry.ReplayId,
            entry.Attempt,
            entry.SettledStatus
        );
    }

    private void MarkTerminal(PredictionLedgerEntry entry, string reason)
    {
        entry.State = PredictionLedgerState.Terminal;
        entry.Intent = PredictionIntent.None;
        entry.TerminalReason = reason;
        entry.NextAttemptAt = null;
        ledger.Upsert(entry);
        logger.LogError(
            "Prediction {PredictionId} for replay {ReplayId} attempt {Attempt} hit a terminal settlement error: {Reason}",
            entry.PredictionId,
            entry.ReplayId,
            entry.Attempt,
            reason
        );
    }

    private void RememberChannel(PredictionLedgerEntry entry, Prediction channel)
    {
        if (!string.IsNullOrWhiteSpace(channel.BroadcasterId))
        {
            entry.BroadcasterId = channel.BroadcasterId;
        }

        string blue = FindOutcomeId(channel.Outcomes, MatchPrediction.Blue);
        string red = FindOutcomeId(channel.Outcomes, MatchPrediction.Red);
        if (!string.IsNullOrWhiteSpace(blue))
        {
            entry.BlueOutcomeId = blue;
        }

        if (!string.IsNullOrWhiteSpace(red))
        {
            entry.RedOutcomeId = red;
        }

        currentReplayId = entry.ReplayId;
        ledger.Upsert(entry);
    }

    private PredictionReconcileResult ClassifyRemote(Prediction[] remote)
    {
        var refs = new List<PredictionRemoteRef>();
        if (remote != null)
        {
            foreach (Prediction prediction in remote)
            {
                if (prediction == null || string.IsNullOrWhiteSpace(prediction.Id))
                {
                    continue;
                }

                refs.Add(
                    new PredictionRemoteRef(
                        prediction.Id,
                        prediction.Title,
                        MapStatus(prediction.Status)
                    )
                );
            }
        }

        return PredictionSettlementPolicy.Reconcile(ledger.Entries, refs, remoteFetched: true);
    }

    private void ReportReconcile(PredictionReconcileResult result, Prediction[] remote)
    {
        foreach (string foreign in result.ForeignPredictionIds)
        {
            Prediction match = FindPrediction(remote, foreign);
            logger.LogWarning(
                "Orphaned Twitch prediction {PredictionId} ({Title}, {Status}) has no ledger entry. Leaving it alone.",
                foreign,
                match?.Title,
                match == null ? null : match.Status.ToString()
            );
        }

        foreach (string orphan in result.OrphanedPredictionIds)
        {
            PredictionLedgerEntry entry = ledger.FindByPredictionId(orphan);
            logger.LogWarning(
                "Ledger prediction {PredictionId} for replay {ReplayId} attempt {Attempt} was not in the Twitch list. Not guessing an outcome.",
                orphan,
                entry?.ReplayId,
                entry?.Attempt
            );
        }
    }

    private bool LiveHelix() =>
        settings.Twitch.EnablePredictions
        && settings.Capture.Method != CaptureMethod.None
        && !settings.Twitch.DryRunMode;

    private void LogDrySettle(int? team)
    {
        if (team.HasValue)
        {
            logger.LogInformation(
                "Dry-run prediction resolve -> {Outcome}.",
                MatchPrediction.OutcomeTitle(team.Value)
            );
            return;
        }

        logger.LogInformation("Dry-run prediction cancel (no winner).");
    }

    private async Task<Prediction[]> FetchChannelAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string channelId = await GetChannelIdAsync().ConfigureAwait(false);
        var existing = await api
            .Helix.Predictions.GetPredictionsAsync(channelId)
            .ConfigureAwait(false);
        return existing?.Data ?? Array.Empty<Prediction>();
    }

    private async Task<Prediction> FetchOneAsync(
        PredictionLedgerEntry entry,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        string channelId = entry.BroadcasterId;
        if (string.IsNullOrWhiteSpace(channelId))
        {
            channelId = await GetChannelIdAsync().ConfigureAwait(false);
            entry.BroadcasterId = channelId;
        }

        var fetched = await api
            .Helix.Predictions.GetPredictionsAsync(
                channelId,
                new List<string> { entry.PredictionId }
            )
            .ConfigureAwait(false);
        return FindPrediction(fetched?.Data, entry.PredictionId);
    }

    private async Task<string> GetChannelIdAsync()
    {
        if (!string.IsNullOrWhiteSpace(cachedChannelId))
        {
            return cachedChannelId;
        }

        string login = string.IsNullOrWhiteSpace(settings.Twitch.Channel)
            ? settings.Twitch.Account
            : settings.Twitch.Channel;
        var users = await api
            .Helix.Users.GetUsersAsync(logins: new List<string> { login })
            .ConfigureAwait(false);
        if (users?.Users == null || users.Users.Length == 0)
        {
            throw new InvalidOperationException($"Helix returned no user for `{login}`.");
        }

        cachedChannelId = users.Users[0].Id;
        return cachedChannelId;
    }

    private static Prediction ActiveOrLocked(Prediction[] predictions)
    {
        if (predictions == null)
        {
            return null;
        }

        foreach (Prediction prediction in predictions)
        {
            if (
                prediction != null
                && (
                    prediction.Status == PredictionStatus.ACTIVE
                    || prediction.Status == PredictionStatus.LOCKED
                )
            )
            {
                return prediction;
            }
        }

        return null;
    }

    private static Prediction FindPrediction(Prediction[] predictions, string predictionId)
    {
        if (predictions == null || string.IsNullOrWhiteSpace(predictionId))
        {
            return null;
        }

        foreach (Prediction prediction in predictions)
        {
            if (
                prediction != null
                && string.Equals(prediction.Id, predictionId, StringComparison.Ordinal)
            )
            {
                return prediction;
            }
        }

        return null;
    }

    private static Prediction First(Prediction[] predictions)
    {
        if (predictions == null)
        {
            return null;
        }

        foreach (Prediction prediction in predictions)
        {
            if (prediction != null && !string.IsNullOrWhiteSpace(prediction.Id))
            {
                return prediction;
            }
        }

        return null;
    }

    private static Prediction FirstEnded(Prediction[] predictions)
    {
        if (predictions == null)
        {
            return null;
        }

        foreach (Prediction prediction in predictions)
        {
            if (prediction == null)
            {
                continue;
            }

            if (
                prediction.Status is PredictionStatus.RESOLVED or PredictionStatus.CANCELED
                || (prediction.Outcomes != null && prediction.Outcomes.Length > 0)
            )
            {
                return prediction;
            }
        }

        return null;
    }

    private static PredictionRemoteState MapStatus(PredictionStatus status) =>
        status switch
        {
            PredictionStatus.ACTIVE => PredictionRemoteState.Active,
            PredictionStatus.LOCKED => PredictionRemoteState.Locked,
            PredictionStatus.RESOLVED => PredictionRemoteState.Resolved,
            PredictionStatus.CANCELED => PredictionRemoteState.Canceled,
            _ => PredictionRemoteState.Unknown,
        };

    private static string FindOutcomeId(
        TwitchLib.Api.Helix.Models.Predictions.Outcome[] outcomes,
        string title
    )
    {
        if (outcomes == null)
        {
            return null;
        }

        foreach (var outcome in outcomes)
        {
            if (
                outcome != null
                && string.Equals(outcome.Title, title, StringComparison.OrdinalIgnoreCase)
            )
            {
                return outcome.Id;
            }
        }

        return null;
    }
}
