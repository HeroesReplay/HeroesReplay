using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Context;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.HeroesProfileExtension;

public class TalentNotifier : ITalentNotifier
{
    private readonly ILogger<TalentNotifier> logger;
    private readonly IReplayContext context;
    private readonly ITwitchExtensionService extensionService;
    private readonly AppSettings settings;
    private readonly SemaphoreSlim sendLock = new(1, 1);

    private string gameId;
    private int seq;
    private string lastHash;
    private string pendingHash;
    private DateTime lastSentUtc = DateTime.MinValue;
    private bool stopped;
    private bool missingLogged;

    private ContextData Data => context.Current;

    public TalentNotifier(
        ILogger<TalentNotifier> logger,
        IReplayContext context,
        ITwitchExtensionService extensionService,
        AppSettings settings
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.extensionService =
            extensionService ?? throw new ArgumentNullException(nameof(extensionService));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void ClearSession()
    {
        gameId = Guid.NewGuid().ToString();
        seq = 0;
        lastHash = null;
        pendingHash = null;
        lastSentUtc = DateTime.MinValue;
        stopped = false;
        missingLogged = false;
    }

    public Task SendCurrentTalentsAsync(
        TimeSpan timer,
        bool clockLive,
        CancellationToken token = default
    )
    {
        if (stopped || string.IsNullOrEmpty(gameId))
        {
            return Task.CompletedTask;
        }

        ExtensionGame game = CurrentGame();
        if (game == null)
        {
            return Task.CompletedTask;
        }

        string phase = clockLive
            ? ExtensionSnapshotSelector.InGame
            : ExtensionSnapshotSelector.Lobby;
        return SendIfDueAsync(
            ExtensionSnapshotSelector.Select(game, phase, timer),
            bypassInterval: false,
            token
        );
    }

    public Task EndGameAsync(CancellationToken token = default)
    {
        if (stopped || string.IsNullOrEmpty(gameId))
        {
            return Task.CompletedTask;
        }

        ExtensionGame game = CurrentGame();
        if (game == null)
        {
            return Task.CompletedTask;
        }

        return SendIfDueAsync(
            ExtensionSnapshotSelector.Select(game, ExtensionSnapshotSelector.Ended, timer: null),
            bypassInterval: true,
            token
        );
    }

    private ExtensionGame CurrentGame()
    {
        ExtensionGame game = Data?.Payloads;
        if (game?.Players == null || game.Players.Count == 0)
        {
            if (!missingLogged)
            {
                logger.LogWarning("Twitch extension has no players to send for this replay.");
                missingLogged = true;
            }

            return null;
        }

        return game;
    }

    private async Task SendIfDueAsync(
        ExtensionSnapshot snapshot,
        bool bypassInterval,
        CancellationToken token
    )
    {
        await sendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (stopped)
            {
                return;
            }

            string hash = ExtensionSnapshotSelector.Hash(snapshot);
            if (hash == lastHash)
            {
                return;
            }

            if (
                !bypassInterval
                && lastSentUtc != DateTime.MinValue
                && DateTime.UtcNow - lastSentUtc < MinInterval()
            )
            {
                return;
            }

            if (hash != pendingHash)
            {
                seq++;
                pendingHash = hash;
            }

            ExtensionPostOutcome outcome = await extensionService
                .PostSnapshotAsync(gameId, seq, snapshot, token)
                .ConfigureAwait(false);
            if (outcome == ExtensionPostOutcome.Sent)
            {
                lastHash = hash;
                lastSentUtc = DateTime.UtcNow;
                logger.LogInformation(
                    "Twitch extension {Phase} seq {Seq} sent.",
                    snapshot.Phase,
                    seq
                );
            }
            else if (outcome == ExtensionPostOutcome.Stopped)
            {
                stopped = true;
                logger.LogWarning("Twitch extension updates stopped for this replay.");
            }
        }
        finally
        {
            sendLock.Release();
        }
    }

    private TimeSpan MinInterval()
    {
        TimeSpan interval = settings.TwitchExtension?.MinInterval ?? TimeSpan.Zero;
        return interval > TimeSpan.Zero ? interval : TimeSpan.FromSeconds(8);
    }
}
