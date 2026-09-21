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
    private readonly object gate = new object();
    private string broadcasterId;
    private string predictionId;
    private string blueOutcomeId;
    private string redOutcomeId;

    public TwitchMatchPredictionService(
        ILogger<TwitchMatchPredictionService> logger,
        AppSettings settings,
        ITwitchAPI api
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public Task StartAsync(LoadedReplay replay, CancellationToken cancellationToken) =>
        OpenAsync(replay?.Replay?.Map, cancellationToken);

    public async Task OpenAsync(string map, CancellationToken cancellationToken)
    {
        if (!settings.Twitch.EnablePredictions || settings.Capture.Method == CaptureMethod.None)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await CancelActiveAsync(cancellationToken).ConfigureAwait(false);

        string channelId = await GetChannelIdAsync().ConfigureAwait(false);
        bool channelClear = await CancelChannelPredictionAsync(channelId, cancellationToken)
            .ConfigureAwait(false);
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
            return;
        }

        if (!channelClear)
        {
            return;
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
            return;
        }
        Prediction prediction = created?.Data?[0];
        if (prediction == null || string.IsNullOrWhiteSpace(prediction.Id))
        {
            logger.LogWarning("Helix CreatePrediction returned no prediction.");
            return;
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

            await api
                .Helix.Predictions.EndPredictionAsync(
                    channelId,
                    id,
                    PredictionEndStatus.RESOLVED,
                    winningId
                )
                .ConfigureAwait(false);
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

    private async Task<bool> CancelChannelPredictionAsync(
        string channelId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (settings.Twitch.DryRunMode || string.IsNullOrWhiteSpace(channelId))
        {
            return true;
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
                return true;
            }

            if (active.Status == PredictionStatus.LOCKED)
            {
                logger.LogWarning(
                    "Prediction {PredictionId} is locked, so a new one cannot be opened yet.",
                    active.Id
                );
                return false;
            }

            await api
                .Helix.Predictions.EndPredictionAsync(
                    channelId,
                    active.Id,
                    PredictionEndStatus.CANCELED
                )
                .ConfigureAwait(false);
            logger.LogInformation(
                "Canceled the channel prediction {PredictionId} that was already open.",
                active.Id
            );
            return true;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not clear the prediction already open on the channel.");
            return false;
        }
    }

    private async Task CancelActiveAsync(CancellationToken cancellationToken)
    {
        string id;
        string channelId;
        lock (gate)
        {
            id = predictionId;
            channelId = broadcasterId;
        }

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(channelId))
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
        }
    }
}
