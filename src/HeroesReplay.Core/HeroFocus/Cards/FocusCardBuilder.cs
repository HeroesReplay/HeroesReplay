using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Builds.Matching;
using HeroesReplay.Core.Builds.Played;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Metadata;

namespace HeroesReplay.Core.HeroFocus.Cards;

/// <summary>
/// What the replay file alone does not say: its Heroes Profile id, rank and average MMR, and the
/// map name Heroes Profile gives it. Any of them may be missing.
/// </summary>
public sealed record FocusReplayFacts(
    string Key,
    int? ReplayId = null,
    string File = null,
    string Rank = null,
    double? AverageMmr = null,
    string HeroesProfileMap = null
);

/// <summary>
/// Reads a parsed replay (events and statistics on) into a <see cref="FocusCard"/> (#433): each
/// player's catalog hero, role, score screen, awards, closest guide build, and both teams'
/// composition labels. Heroes come from the catalog by their spawned unit, so names are English
/// whatever the replay's game language.
/// </summary>
public sealed class FocusCardBuilder
{
    private readonly IGuideCatalog guides;
    private readonly BuildMatcher matcher;
    private readonly YouTubeTitleSettings titles;

    public FocusCardBuilder(
        IGuideCatalog guides,
        BuildMatcher matcher = null,
        YouTubeTitleSettings titles = null
    )
    {
        this.guides = guides ?? throw new ArgumentNullException(nameof(guides));
        this.matcher = matcher ?? new BuildMatcher();
        this.titles = titles ?? new YouTubeTitleSettings();
    }

    public FocusCard Build(
        Replay replay,
        IReadOnlyList<Hero> catalog,
        FocusReplayFacts facts,
        DateTime nowUtc
    )
    {
        if (replay == null)
        {
            throw new ArgumentNullException(nameof(replay));
        }

        if (string.IsNullOrWhiteSpace(facts?.Key))
        {
            throw new ArgumentException("A card needs a key.", nameof(facts));
        }

        catalog ??= Array.Empty<Hero>();
        bool aram = replay.GameMode == GameMode.ARAM;
        var seats = (replay.Players ?? Array.Empty<Player>())
            .Select((player, index) => (Player: player, Index: index))
            .Where(seat => seat.Player != null && seat.Player.PlayerType != PlayerType.Spectator)
            .Select(seat =>
                (seat.Player, seat.Index, Hero: PlayedHero.Find(catalog, replay, seat.Player))
            )
            .ToList();

        IReadOnlyList<ReplayMediaPlayer> roster = seats
            .Select(seat => new ReplayMediaPlayer
            {
                Team = seat.Player.Team,
                Hero = seat.Hero?.Name,
                IsAi = seat.Player.PlayerType == PlayerType.Computer,
            })
            .ToList();
        var comps = new Dictionary<int, List<string>>
        {
            [0] = Comp(catalog, roster, 0),
            [1] = Comp(catalog, roster, 1),
        };

        int? winner = seats.FirstOrDefault(seat => seat.Player.IsWinner).Player?.Team;
        Player regional = seats
            .Select(seat => seat.Player)
            .FirstOrDefault(player =>
                player.PlayerType == PlayerType.Human && player.BattleNetRegionId > 0
            );

        return new FocusCard
        {
            Key = facts.Key.Trim(),
            ReplayId = facts.ReplayId,
            File = facts.File,
            Map = EnglishMapNames.Prefer(
                facts.HeroesProfileMap,
                replay.Map,
                replay.MapAlternativeName
            ),
            GameMode = replay.GameMode.ToString(),
            Rank = string.IsNullOrWhiteSpace(facts.Rank) ? null : facts.Rank.Trim(),
            AverageMmr = facts.AverageMmr,
            Region = RegionName(regional?.BattleNetRegionId ?? 0),
            GameVersion = replay.ReplayVersion,
            GameDateUtc =
                replay.Timestamp == default
                    ? null
                    : DateTime.SpecifyKind(replay.Timestamp, DateTimeKind.Utc),
            LengthSeconds = Math.Round(replay.ReplayLength.TotalSeconds),
            WinnerTeam = winner,
            GuideCheckedAt = guides.Reference?.CheckedAt?.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            ),
            BuiltAtUtc = nowUtc,
            Players = seats
                .Select(seat => Player(seat.Player, seat.Index, seat.Hero, winner, aram, comps))
                .ToList(),
        };
    }

    /// <summary>NA, EU, KR or CN for a Battle.net region id; null for PTR or an unknown one.</summary>
    public static string RegionName(int regionId) =>
        regionId switch
        {
            1 => "NA",
            2 => "EU",
            3 => "KR",
            5 => "CN",
            _ => null,
        };

    private FocusCardPlayer Player(
        Player player,
        int index,
        Hero hero,
        int? winner,
        bool aram,
        IReadOnlyDictionary<int, List<string>> comps
    )
    {
        bool ai = player.PlayerType == PlayerType.Computer;
        return new FocusCardPlayer
        {
            Index = index,
            Team = player.Team,
            Won = winner != null ? winner == player.Team : player.IsWinner,
            IsAi = ai,
            Name = string.IsNullOrWhiteSpace(player.Name) ? null : player.Name.Trim(),
            BattleTag = player.BattleTag,
            RegionId = player.BattleNetRegionId,
            Hero = hero?.Name,
            AttributeId = hero?.AttributeId,
            UnitId = hero?.UnitId,
            Role = hero?.Role,
            Stats = ScoreStats.Read(player.ScoreResult),
            Awards = ScoreStats.Awards(player.ScoreResult),
            Build = ai || hero == null ? null : BuildOf(hero, player, aram),
            AllyComp = comps.TryGetValue(player.Team, out List<string> ally) ? ally : new(),
            EnemyComp = comps.TryGetValue(1 - player.Team, out List<string> enemy) ? enemy : new(),
        };
    }

    private FocusCardBuild BuildOf(Hero hero, Player player, bool aram)
    {
        BuildMatch match = matcher.Match(PlayedBuilds.Read(hero, player), guides.Find(hero), aram);
        return match == null
            ? null
            : new FocusCardBuild
            {
                Name = match.Best.Build.Name,
                Tag = match.Best.Build.Tag,
                Score = Math.Round(match.Best.Score, 3),
                Ambiguous = match.Ambiguous,
                Tiers = match.Best.Tiers,
            };
    }

    private List<string> Comp(
        IReadOnlyList<Hero> catalog,
        IReadOnlyList<ReplayMediaPlayer> roster,
        int team
    ) => TeamComposition.Labels(catalog, roster, team, titles).Select(label => label.Key).ToList();
}
