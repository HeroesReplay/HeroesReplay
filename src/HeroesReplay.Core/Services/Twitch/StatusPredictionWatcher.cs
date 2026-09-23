using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Status;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Interfaces;

namespace HeroesReplay.Core.Services.Twitch;

public sealed class StatusPredictionWatcher
{
    private readonly ILogger<StatusPredictionWatcher> logger;
    private readonly AppSettings settings;
    private readonly SpectatorStatusStore statusStore;
    private readonly IMatchPredictionService predictions;
    private readonly ITwitchClient twitchClient;

    public StatusPredictionWatcher(
        ILogger<StatusPredictionWatcher> logger,
        AppSettings settings,
        SpectatorStatusStore statusStore,
        IMatchPredictionService predictions,
        ITwitchClient twitchClient
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.statusStore = statusStore ?? throw new ArgumentNullException(nameof(statusStore));
        this.predictions = predictions ?? throw new ArgumentNullException(nameof(predictions));
        this.twitchClient = twitchClient ?? throw new ArgumentNullException(nameof(twitchClient));
    }

    public async Task WatchAsync(CancellationToken cancellationToken)
    {
        bool enabled =
            settings.Twitch.EnablePredictions && settings.Capture.Method != CaptureMethod.None;
        if (!enabled)
        {
            logger.LogInformation(
                "Match predictions are off (Twitch:EnablePredictions false, or capture none)."
            );
        }
        else
        {
            logger.LogInformation(
                "Watching {StatusFile} for Blue/Red predictions.",
                statusStore.FilePath
            );
        }

        var tracker = new PredictionSessionTracker();
        while (!cancellationToken.IsCancellationRequested)
        {
            if (enabled)
            {
                try
                {
                    await StepAsync(tracker, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Prediction watch step failed.");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StepAsync(
        PredictionSessionTracker tracker,
        CancellationToken cancellationToken
    )
    {
        SpectatorStatus status = statusStore.TryReadShared();
        if (status == null)
        {
            return;
        }

        PredictionSignal signal = tracker.Observe(status, DateTimeOffset.UtcNow);
        switch (signal.Kind)
        {
            case PredictionSignalKind.Open:
                logger.LogInformation(
                    "Opening Blue/Red prediction for replay {ReplayId} ({Map}).",
                    signal.ReplayId,
                    signal.Map
                );
                bool opened = await predictions
                    .OpenAsync(signal.Map, cancellationToken)
                    .ConfigureAwait(false);
                if (!opened)
                {
                    tracker.Release(signal.ReplayId);
                }

                break;
            case PredictionSignalKind.Resolve:
                logger.LogInformation(
                    "Resolving prediction for replay {ReplayId} -> team {Team}.",
                    signal.ReplayId,
                    signal.WinnerTeam
                );
                await predictions
                    .ResolveTeamAsync(signal.WinnerTeam, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PredictionSignalKind.Cancel:
                logger.LogInformation(
                    "Canceling prediction for replay {ReplayId} (no winner published).",
                    signal.ReplayId
                );
                await predictions.ResolveTeamAsync(null, cancellationToken).ConfigureAwait(false);
                break;
            case PredictionSignalKind.Disabled:
                logger.LogInformation(
                    "Prediction disabled for viewer-entered replay {ReplayId}.",
                    signal.ReplayId
                );
                AnnouncePredictionDisabled();
                break;
        }
    }

    private void AnnouncePredictionDisabled()
    {
        if (!settings.Twitch.EnableChatBot || string.IsNullOrWhiteSpace(settings.Twitch.Channel))
        {
            return;
        }

        try
        {
            twitchClient.SendMessage(
                settings.Twitch.Channel,
                "Prediction disabled for this replay.",
                settings.Twitch.DryRunMode
            );
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not announce that the prediction is disabled.");
        }
    }
}
