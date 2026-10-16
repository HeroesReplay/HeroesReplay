using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.HeroFocus.Cards;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.HeroFocus;

/// <summary>
/// The download role's focus work (#433), beside the download loop and never holding it back:
/// every <c>Focus:RefreshInterval</c>, the focus cards of new waiting replays, a few Heroes Profile
/// records for the waiting cards, and the privacy feed when it is due. A failed step is logged and
/// the next one runs on time. Measured on ASA-SERVER (2026-10-16): a cold record waits about 10 s
/// for Heroes Profile's job, so 10 records take about 2 minutes, and the cards are built first.
/// </summary>
public sealed class HeroFocusDownload
{
    private readonly FocusCardRefresh cards;
    private readonly FocusStrengthRefresh strength;
    private readonly PlayerPrivacySync privacy;
    private readonly HeroFocusSettings settings;
    private readonly ILogger<HeroFocusDownload> logger;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public HeroFocusDownload(
        FocusCardRefresh cards,
        FocusStrengthRefresh strength,
        PlayerPrivacySync privacy,
        HeroFocusSettings settings,
        ILogger<HeroFocusDownload> logger,
        Func<TimeSpan, CancellationToken, Task> delay = null
    )
    {
        this.cards = cards ?? throw new ArgumentNullException(nameof(cards));
        this.strength = strength ?? throw new ArgumentNullException(nameof(strength));
        this.privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));
        this.settings = settings ?? new HeroFocusSettings();
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.delay = delay ?? ((wait, token) => Task.Delay(wait, token));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        TimeSpan interval =
            settings.RefreshInterval > TimeSpan.Zero
                ? settings.RefreshInterval
                : TimeSpan.FromSeconds(30);
        logger.LogInformation(
            "Focus cards are on: up to {Cards} cards and {Lookups} Heroes Profile records every {Interval}, into {Folder}.",
            settings.CardsPerStep,
            settings.StrengthLookupsPerStep,
            interval,
            cards.Cards.Folder
        );
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await StepAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    e,
                    "A focus card step failed. The next runs in {Interval}.",
                    interval
                );
            }

            try
            {
                await delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Up to <c>CardsPerStep</c> missing cards, then records for the waiting cards, then the
    /// privacy feed when it is due.
    /// </summary>
    public async Task StepAsync(CancellationToken cancellationToken)
    {
        for (int built = 0; built < Math.Max(1, settings.CardsPerStep); built++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cards.Step() == null)
            {
                break;
            }
        }

        await strength
            .StepAsync(cards.Cards, cards.Waiting().Select(facts => facts.Key), cancellationToken)
            .ConfigureAwait(false);
        if (privacy.IsDue())
        {
            await privacy.SyncAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
