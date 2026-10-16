using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.HeroFocus.Scoring;
using HeroesReplay.Core.HeroFocus.Signatures;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.HeroFocus.Cards;

/// <summary>
/// Keeps a focus card for each Standard replay waiting to be spectated (#433), one card a step,
/// newest replay first, in the download role, the cards' only writer. A card is built again when
/// its <see cref="FocusCard.Version"/> or the talent-build reference it was matched against
/// changes. A step first deletes the cards of replays that are gone or will not play (spectated,
/// below the patch floor, quarantined). Each new card's signature stats join the hero baselines.
/// </summary>
public sealed class FocusCardRefresh
{
    private readonly FocusCardBuilder builder;
    private readonly FocusCardStore cards;
    private readonly HeroBaselineStore baselines;
    private readonly HeroSignatures signatures;
    private readonly HeroFocusSettings settings;
    private readonly Func<IReadOnlyList<Hero>> catalog;
    private readonly Func<string, Replay> parse;
    private readonly string standardDirectory;
    private readonly string dataDirectory;
    private readonly string guideCheckedAt;
    private readonly ILogger logger;
    private readonly Func<DateTime> clock;
    private readonly HashSet<string> unreadable = new(StringComparer.OrdinalIgnoreCase);

    public FocusCardRefresh(
        FocusCardBuilder builder,
        FocusCardStore cards,
        HeroBaselineStore baselines,
        HeroSignatures signatures,
        HeroFocusSettings settings,
        Func<IReadOnlyList<Hero>> catalog,
        Func<string, Replay> parse,
        string standardDirectory,
        string dataDirectory,
        string guideCheckedAt,
        ILogger logger,
        Func<DateTime> clock = null
    )
    {
        this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
        this.cards = cards ?? throw new ArgumentNullException(nameof(cards));
        this.baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
        this.signatures = signatures ?? throw new ArgumentNullException(nameof(signatures));
        this.settings = settings ?? new HeroFocusSettings();
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.parse = parse ?? throw new ArgumentNullException(nameof(parse));
        this.standardDirectory = standardDirectory;
        this.dataDirectory = dataDirectory;
        this.guideCheckedAt = guideCheckedAt;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.clock = clock ?? (() => DateTime.UtcNow);
    }

    public FocusCardStore Cards => cards;

    /// <summary>The replay files a card is kept for: waiting Standard replays, newest id first.</summary>
    public IReadOnlyList<FocusReplayFacts> Waiting()
    {
        if (string.IsNullOrWhiteSpace(standardDirectory) || !Directory.Exists(standardDirectory))
        {
            return Array.Empty<FocusReplayFacts>();
        }

        HashSet<int> done = SpectateQueue.ReadIds(
            dataDirectory,
            SpectateQueue.SpectatedFileName,
            SpectateQueue.BelowFloorFileName,
            SpectateQueue.QuarantineFileName
        );
        return Directory
            .EnumerateFiles(standardDirectory, "*.StormReplay")
            .Select(FocusReplayFacts.FromFile)
            .Where(facts => facts.ReplayId is int id && !done.Contains(id))
            .GroupBy(facts => facts.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(facts => facts.ReplayId)
            .ToList();
    }

    /// <summary>
    /// Deletes the cards no waiting replay needs, then builds one missing or stale card.
    /// Returns the card built, or null when every waiting replay has a current card.
    /// </summary>
    public FocusCard Step()
    {
        IReadOnlyList<FocusReplayFacts> waiting = Waiting();
        var keep = new HashSet<string>(waiting.Select(facts => facts.Key), StringComparer.Ordinal);
        foreach (string key in cards.Keys().Where(key => !keep.Contains(key)))
        {
            cards.Delete(key);
        }

        foreach (FocusReplayFacts facts in waiting)
        {
            if (unreadable.Contains(facts.Key) || IsCurrent(cards.Read(facts.Key)))
            {
                continue;
            }

            FocusCard built = Build(facts);
            if (built != null)
            {
                return built;
            }
        }

        return null;
    }

    /// <summary>A card of this version, matched against this talent-build reference.</summary>
    public bool IsCurrent(FocusCard card) =>
        card != null
        && card.Version == FocusCard.CurrentVersion
        && string.Equals(card.GuideCheckedAt, guideCheckedAt, StringComparison.Ordinal);

    private FocusCard Build(FocusReplayFacts facts)
    {
        string path = Path.Combine(standardDirectory, facts.File);
        Replay replay = parse(path);
        if (replay?.Players == null || replay.Players.Length == 0)
        {
            unreadable.Add(facts.Key);
            logger.LogInformation(
                "Replay {ReplayId} did not parse; it gets no focus card.",
                facts.ReplayId
            );
            return null;
        }

        FocusCard card = builder.Build(replay, catalog(), facts, clock());
        cards.Write(card);
        HeroBaselines known = baselines.Read();
        if (known.Add(card, signatures, settings.BaselineWindow))
        {
            baselines.Write(known);
        }

        logger.LogInformation(
            "Focus card for replay {ReplayId}: {Map}, {Players} players.",
            card.ReplayId,
            card.Map,
            card.Players.Count
        );
        return card;
    }
}
