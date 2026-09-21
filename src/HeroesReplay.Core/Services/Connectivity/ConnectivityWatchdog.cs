using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.Core.Services.Shared;
using HeroesReplay.Core.Services.Status;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Connectivity;

public sealed class ConnectivityWatchdog : IConnectivityWatchdog
{
    private readonly ILogger<ConnectivityWatchdog> logger;
    private readonly AppSettings settings;
    private readonly INetworkProbe probe;
    private readonly SpectatorStatusStore statusStore;
    private readonly CancellationTokenProvider tokenProvider;
    private readonly IObsController obsController;
    private readonly IHeroesProfileResume heroesProfileResume;
    private readonly object gate = new();
    private int failCount;
    private int recoverCount;
    private string lastWrittenDetail;
    private bool? lastWrittenOnline;

    public ConnectivityWatchdog(
        ILogger<ConnectivityWatchdog> logger,
        AppSettings settings,
        INetworkProbe probe,
        SpectatorStatusStore statusStore,
        CancellationTokenProvider tokenProvider,
        IObsController obsController = null,
        IHeroesProfileResume heroesProfileResume = null
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.probe = probe ?? throw new ArgumentNullException(nameof(probe));
        this.statusStore = statusStore ?? throw new ArgumentNullException(nameof(statusStore));
        this.tokenProvider =
            tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        this.obsController = obsController;
        this.heroesProfileResume = heroesProfileResume;
        IsOnline = true;
        Last = new ConnectivitySnapshot
        {
            At = DateTimeOffset.UtcNow,
            Internet = true,
            Twitch = true,
            HeroesProfile = true,
        };
    }

    public bool IsOnline { get; private set; }
    public ConnectivitySnapshot Last { get; private set; }
    public event EventHandler<ConnectivityChangedEventArgs> Changed;

    public async Task<ConnectivitySnapshot> ProbeAsync(CancellationToken cancellationToken)
    {
        bool internet = await probe.ProbeInternetAsync(cancellationToken).ConfigureAwait(false);
        bool twitch = await probe.ProbeTwitchAsync(cancellationToken).ConfigureAwait(false);
        bool heroesProfile = await probe
            .ProbeHeroesProfileAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ConnectivitySnapshot
        {
            At = DateTimeOffset.UtcNow,
            Internet = internet,
            Twitch = twitch,
            HeroesProfile = heroesProfile,
        };
    }

    public bool Apply(ConnectivitySnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        ConnectivityChangedEventArgs changed = null;
        lock (gate)
        {
            Last = snapshot;
            if (snapshot.Internet)
            {
                failCount = 0;
                recoverCount++;
            }
            else
            {
                recoverCount = 0;
                failCount++;
            }

            int failThreshold = Math.Max(1, Settings.FailThreshold);
            int recoverThreshold = Math.Max(1, Settings.RecoverThreshold);

            if (IsOnline && failCount >= failThreshold)
            {
                IsOnline = false;
                changed = new ConnectivityChangedEventArgs
                {
                    IsOnline = false,
                    Snapshot = snapshot,
                };
            }
            else if (!IsOnline && recoverCount >= recoverThreshold)
            {
                IsOnline = true;
                changed = new ConnectivityChangedEventArgs { IsOnline = true, Snapshot = snapshot };
            }
        }

        string detail = snapshot.Describe();
        if (changed != null || lastWrittenOnline != IsOnline || lastWrittenDetail != detail)
        {
            lastWrittenOnline = IsOnline;
            lastWrittenDetail = detail;
            statusStore.Patch(status =>
            {
                status.ConnectivityOnline = IsOnline;
                status.Connectivity = detail;
            });
        }

        if (changed == null)
        {
            return false;
        }

        ConnectivityResume.Decision decision = ConnectivityResume.Decide(
            wasOnline: !changed.IsOnline,
            isOnline: changed.IsOnline,
            streamingEnabled: SessionMedia.ShouldStream(settings.OBS)
        );

        logger.LogWarning(
            "Connectivity {State}: {Detail}",
            changed.IsOnline ? "restored" : "lost",
            snapshot.Describe()
        );
        if (decision.RetryHeroesProfile)
        {
            heroesProfileResume?.Arm();
            logger.LogInformation(
                "Connectivity restored. Retrying Heroes Profile list/download once."
            );
        }

        HandleStream(decision);
        Changed?.Invoke(this, changed);
        return true;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken == default)
        {
            cancellationToken = tokenProvider.Token;
        }

        if (!Settings.Enabled)
        {
            logger.LogInformation("Connectivity watchdog disabled.");
            return;
        }

        logger.LogInformation(
            "Connectivity watchdog probing {Host}, Twitch, and Heroes Profile every {Interval}. StreamingEnabled={StreamingEnabled}.",
            Settings.InternetHost,
            Settings.Interval,
            settings.OBS?.StreamingEnabled == true
        );

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                ConnectivitySnapshot snapshot = await ProbeAsync(cancellationToken)
                    .ConfigureAwait(false);
                Apply(snapshot);
                await Task.Delay(Settings.Interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Connectivity probe failed.");
                await Task.Delay(Settings.Interval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private ConnectivitySettings Settings => settings.Connectivity ?? new ConnectivitySettings();

    private void HandleStream(ConnectivityResume.Decision decision)
    {
        if (obsController == null || !SessionMedia.ShouldStream(settings.OBS))
        {
            return;
        }

        try
        {
            if (decision.StartStream)
            {
                obsController.StartStreaming();
            }
            else
            {
                obsController.StopStreaming();
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Could not {Action} OBS stream after connectivity change.",
                decision.StartStream ? "start" : "stop"
            );
        }
    }
}
