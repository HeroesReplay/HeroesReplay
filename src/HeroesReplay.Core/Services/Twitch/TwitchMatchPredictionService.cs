using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly object gate = new object();
    private string broadcasterId;
    private string predictionId;
    private string blueOutcomeId;
    private string redOutcomeId;
    private bool adoptedLocked;

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
    }

    public Task StartAsync(LoadedReplay replay, CancellationToken cancellationToken) =>
        OpenAsync(
            EnglishMapNames.Prefer(null, replay?.Replay?.Map, replay?.Replay?.MapAlternativeName),
            cancellationToken
        );

    public async Task<bool> OpenAsync(string map, CancellationToken cancellationToken)
    {
        if (!settings.Twitch.EnablePredictions || settings.Capture.Method == CaptureMethod.None)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        string title = MatchPrediction.TitleForMap(map);
        reports.TryWriteCurrent(title);
        await CancelActiveAsync(cancellationToken).ConfigureAwait(false);

        string channelId = await GetChannelIdAsync().ConfigureAwait(false);
        ChannelPrediction channel = await CancelChannelPredictionAsync(
            channelId,
            title,
            cancellationToken
        )
            .ConfigureAwait(false);
        if (channel == ChannelPrediction.Adopted)
        {
            return true;
        }

        if (channel == ChannelPrediction.Blocked)
        {
            return false;
        }

        var request = new CreatePredictionRequest
        {
            BroadcasterId = channelId,
            Title = MatchPrediction.TitleForMap(map),
            PredictionWindowSeconds = MatchPrediction.WindowSeconds(
                settings.Twitch.PredictionWindow
            ),
            Outcomes = new[]
            {
                new CreateOutcome { Title = MatchPrediction.Blue },
                new CreateOutcome { Title = MatchPrediction.Red },
            },
        };

        if (settings.Twitch.DryRunMode)
        {
            logger.LogInformation(
                "Dry-run prediction: {Title} ({Window}s) Blue vs Red.",
                request.Title,
                request.PredictionWindowSeconds
            );
            return true;
        }

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
        Prediction prediction = created?.Data?.FirstOrDefault();
        if (prediction == null || string.IsNullOrWhiteSpace(prediction.Id))
        {
            logger.LogWarning("Helix CreatePrediction returned no prediction.");
            return false;
        }

        string blue = FindOutcomeId(prediction.Outcomes, MatchPrediction.Blue);
        string red = FindOutcomeId(prediction.Outcomes, MatchPrediction.Red);
        lock (gate)
        {
            broadcasterId = channelId;
            predictionId = prediction.Id;
            blueOutcomeId = blue;
            redOutcomeId = red;
        }

        logger.LogInformation(
            "Opened Blue/Red prediction {PredictionId} ({Title}, {Window}s).",
            prediction.Id,
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
            await OpenAsync(dummy.Replay.Map, cancellationToken).ConfigureAwait(false);
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
        return ResolveTeamAsync(team, cancellationToken);
    }

    public async Task ResolveTeamAsync(int? team, CancellationToken cancellationToken)
    {
        string id;
        string channelId;
        string blue;
        string red;
        lock (gate)
        {
            id = predictionId;
            channelId = broadcasterId;
            blue = blueOutcomeId;
            red = redOutcomeId;
        }

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(channelId))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!team.HasValue)
            {
                if (settings.Twitch.DryRunMode)
                {
                    logger.LogInformation(
                        "Dry-run prediction cancel {PredictionId} (no winner).",
                        id
                    );
                    return;
                }

                await api
                    .Helix.Predictions.EndPredictionAsync(
                        channelId,
                        id,
                        PredictionEndStatus.CANCELED
                    )
                    .ConfigureAwait(false);
                logger.LogInformation("Canceled prediction {PredictionId} (no winner).", id);
                return;
            }

            string winningId = team.Value == 0 ? blue : red;
            if (string.IsNullOrWhiteSpace(winningId))
            {
                logger.LogWarning(
                    "Prediction {PredictionId} missing outcome id for team {Team}.",
                    id,
                    team.Value
                );
                return;
            }

            if (settings.Twitch.DryRunMode)
            {
                logger.LogInformation(
                    "Dry-run prediction resolve {PredictionId} -> {Outcome}.",
                    id,
                    MatchPrediction.OutcomeTitle(team.Value)
                );
                return;
            }

            var ended = await api
                .Helix.Predictions.EndPredictionAsync(
                    channelId,
                    id,
                    PredictionEndStatus.RESOLVED,
                    winningId
                )
                .ConfigureAwait(false);
            Prediction settled = FirstSettled(ended?.Data);
            if (settled == null)
            {
                var fetched = await api
                    .Helix.Predictions.GetPredictionsAsync(channelId, new List<string> { id })
                    .ConfigureAwait(false);
                settled = fetched?.Data?.FirstOrDefault();
            }

            reports.TryWrite(settled);
            logger.LogInformation(
                "Resolved prediction {PredictionId} -> {Outcome}.",
                id,
                MatchPrediction.OutcomeTitle(team.Value)
            );
        }
        finally
        {
            Clear();
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

    private enum ChannelPrediction
    {
        Clear,
        Adopted,
        Blocked,
    }

    private async Task<ChannelPrediction> CancelChannelPredictionAsync(
        string channelId,
        string wantedTitle,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (settings.Twitch.DryRunMode || string.IsNullOrWhiteSpace(channelId))
        {
            return ChannelPrediction.Clear;
        }

        try
        {
            var existing = await api
                .Helix.Predictions.GetPredictionsAsync(channelId)
                .ConfigureAwait(false);
            Prediction active = existing?.Data?.FirstOrDefault(prediction =>
                prediction != null
                && (
                    prediction.Status == PredictionStatus.ACTIVE
                    || prediction.Status == PredictionStatus.LOCKED
                )
            );
            if (active == null)
            {
                return ChannelPrediction.Clear;
            }

            if (active.Status == PredictionStatus.LOCKED)
            {
                if (string.Equals(active.Title, wantedTitle, StringComparison.OrdinalIgnoreCase))
                {
                    lock (gate)
                    {
                        broadcasterId = channelId;
                        predictionId = active.Id;
                        blueOutcomeId = FindOutcomeId(active.Outcomes, MatchPrediction.Blue);
                        redOutcomeId = FindOutcomeId(active.Outcomes, MatchPrediction.Red);
                        adoptedLocked = true;
                    }

                    logger.LogInformation(
                        "Prediction {PredictionId} is already locked for {Title}. It will close with this match.",
                        active.Id,
                        active.Title
                    );
                    return ChannelPrediction.Adopted;
                }

                logger.LogWarning(
                    "Prediction {PredictionId} ({Title}) is locked for a different match than {Wanted}. Twitch will not refund a locked prediction, so a new one cannot open yet.",
                    active.Id,
                    active.Title,
                    wantedTitle
                );
                return ChannelPrediction.Blocked;
            }

            await api
                .Helix.Predictions.EndPredictionAsync(
                    channelId,
                    active.Id,
                    PredictionEndStatus.CANCELED
                )
                .ConfigureAwait(false);
            logger.LogInformation(
                "Canceled prediction {PredictionId}. Channel points for that prediction are refunded.",
                active.Id
            );
            return ChannelPrediction.Clear;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not clear the prediction already open on the channel.");
            return ChannelPrediction.Blocked;
        }
    }

    private async Task CancelActiveAsync(CancellationToken cancellationToken)
    {
        string id;
        string channelId;
        bool locked;
        lock (gate)
        {
            id = predictionId;
            channelId = broadcasterId;
            locked = adoptedLocked;
        }

        // Canceling a locked prediction is not allowed. Leave it for ResolveTeamAsync.
        if (locked || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(channelId))
        {
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!settings.Twitch.DryRunMode)
            {
                await api
                    .Helix.Predictions.EndPredictionAsync(
                        channelId,
                        id,
                        PredictionEndStatus.CANCELED
                    )
                    .ConfigureAwait(false);
            }

            logger.LogInformation("Canceled leftover prediction {PredictionId}.", id);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not cancel leftover prediction {PredictionId}.", id);
        }
        finally
        {
            Clear();
        }
    }

    private async Task<string> GetChannelIdAsync()
    {
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

        return users.Users[0].Id;
    }

    private static Prediction FirstSettled(Prediction[] predictions)
    {
        if (predictions == null)
        {
            return null;
        }

        foreach (Prediction prediction in predictions)
        {
            if (prediction?.Outcomes != null && prediction.Outcomes.Length > 0)
            {
                return prediction;
            }
        }

        return null;
    }

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

    private void Clear()
    {
        lock (gate)
        {
            predictionId = null;
            blueOutcomeId = null;
            redOutcomeId = null;
            adoptedLocked = false;
        }
    }
}
