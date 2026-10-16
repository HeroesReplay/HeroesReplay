using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.HeroFocus.Signatures;
using HeroesReplay.Core.YouTube.Playlists;

namespace HeroesReplay.Core.HeroFocus.Scoring;

/// <summary>
/// Ranks every player of the waiting replays as a focus (#433). A candidate must pass the gates
/// (a human on a catalog hero in a Storm League game, not a hero focused in the last few picks,
/// not a player focused this week, not a hero, build and map already covered, a strong enough
/// record on the hero, and a good enough game). The rest are ranked by
/// <c>quality × (0.5 + novelty)</c>, so a weak game never wins on novelty alone.
/// <list type="bullet">
/// <item>Quality: how well they played (signature stats against the hero's baseline, KDA, kill
/// participation, awards, the win), their Heroes Profile record on the hero, how clearly they
/// played a guide build, and whether the game was a stomp.</item>
/// <item>Novelty: a hero never covered, a role under its share, a new build or enemy comp for the
/// hero, and maps, leagues and regions the recent picks have missed.</item>
/// </list>
/// Pure: the same cards, ledger, baselines and time give the same ranking.
/// </summary>
public sealed class FocusScorer
{
    private const int RoleCount = 6;

    private static readonly HashSet<string> DeathStats = new(StringComparer.Ordinal)
    {
        nameof(ScoreResult.Deaths),
        nameof(ScoreResult.OutnumberedDeaths),
        nameof(ScoreResult.TimeSpentDead),
    };

    private readonly HeroFocusSettings settings;
    private readonly HeroSignatures signatures;

    public FocusScorer(HeroFocusSettings settings, HeroSignatures signatures)
    {
        this.settings = settings ?? new HeroFocusSettings();
        this.signatures = signatures ?? throw new ArgumentNullException(nameof(signatures));
    }

    /// <summary>
    /// Every candidate: the eligible ones best first (ties go to the newer replay, then the lower
    /// player index), then the gated ones. <paramref name="published"/> holds the replay ids
    /// already on YouTube; a published focus counts as full coverage, one only played as half.
    /// </summary>
    public IReadOnlyList<FocusCandidate> Rank(
        IEnumerable<FocusCard> cards,
        FocusLedger ledger,
        HeroBaselines baselines,
        DateTime nowUtc,
        IReadOnlySet<int> published = null,
        bool stormLeagueOnly = true
    )
    {
        ledger ??= new FocusLedger();
        baselines ??= new HeroBaselines();
        IReadOnlyList<FocusLedgerEntry> history = ledger.History;
        var context = new History(
            history,
            history.TakeLast(Math.Max(0, settings.RecentWindow)).ToList(),
            history
                .TakeLast(Math.Max(0, settings.HeroRepeatWindow))
                .Select(entry => entry.AttributeId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            history.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal),
            published ?? new HashSet<int>()
        );

        var candidates = new List<FocusCandidate>();
        foreach (FocusCard card in cards ?? Enumerable.Empty<FocusCard>())
        {
            if (card?.Players == null)
            {
                continue;
            }

            foreach (FocusCardPlayer player in card.Players)
            {
                candidates.Add(Score(card, player, context, baselines, nowUtc, stormLeagueOnly));
            }
        }

        return candidates
            .OrderBy(candidate => candidate.Eligible ? 0 : 1)
            .ThenByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.ReplayId ?? int.MinValue)
            .ThenBy(candidate => candidate.Key, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.PlayerIndex)
            .ToList();
    }

    private FocusCandidate Score(
        FocusCard card,
        FocusCardPlayer player,
        History history,
        HeroBaselines baselines,
        DateTime nowUtc,
        bool stormLeagueOnly
    )
    {
        string build = player.Build?.Name;
        var candidate = new FocusCandidate
        {
            Key = card.Key,
            ReplayId = card.ReplayId,
            PlayerIndex = player.Index,
            Player = player.Player,
            RegionId = player.RegionId,
            Hero = player.Hero,
            AttributeId = player.AttributeId,
            Role = player.Role,
            Build = build,
            BuildScore = player.Build?.Score,
            BuildClear = BuildClarity(player.Build) >= 1,
            Map = card.Map,
            Rank = card.Rank,
            League = YouTubePlaylistNames.League(card.Rank),
            Region = card.Region,
            GameVersion = card.GameVersion,
            AllyComp = CompKey(player.AllyComp),
            EnemyComp = CompKey(player.EnemyComp),
            Strength = player.Strength,
        };

        if (player.IsAi)
        {
            return candidate with { Gate = FocusGates.NotHuman };
        }

        HeroSignature signature = string.IsNullOrWhiteSpace(player.AttributeId)
            ? null
            : signatures.For(player.UnitId, player.Hero, player.Role, build);
        if (signature == null || string.IsNullOrWhiteSpace(player.Role))
        {
            return candidate with { Gate = FocusGates.NoHero };
        }

        var parts = new Dictionary<string, double>(StringComparer.Ordinal);
        double quality = Quality(card, player, signature, baselines, parts);
        double novelty = Novelty(card, candidate, history, nowUtc, parts);
        double score = quality * (0.5 + novelty);
        parts["quality"] = Round(quality);
        parts["novelty"] = Round(novelty);

        return candidate with
        {
            Quality = Round(quality),
            Novelty = Round(novelty),
            Score = Round(score),
            Parts = parts,
            Gate = Gate(card, player, candidate, quality, history, nowUtc, stormLeagueOnly),
        };
    }

    private string Gate(
        FocusCard card,
        FocusCardPlayer player,
        FocusCandidate candidate,
        double quality,
        History history,
        DateTime nowUtc,
        bool stormLeagueOnly
    )
    {
        if (
            stormLeagueOnly
            && !string.Equals(card.GameMode, nameof(GameMode.StormLeague), StringComparison.Ordinal)
        )
        {
            return FocusGates.NotStormLeague;
        }

        if (history.Used.Contains(card.Key))
        {
            return FocusGates.ReplayUsed;
        }

        if (history.Repeat.Contains(player.AttributeId))
        {
            return FocusGates.HeroRepeat;
        }

        if (
            candidate.Player != null
            && history.All.Any(entry =>
                string.Equals(entry.Player, candidate.Player, StringComparison.OrdinalIgnoreCase)
                && entry.RegionId == candidate.RegionId
                && nowUtc - entry.PickedAtUtc < settings.PlayerCooldown
            )
        )
        {
            return FocusGates.PlayerCooldown;
        }

        if (
            candidate.Build != null
            && history.All.Any(entry =>
                SameHero(entry, candidate)
                && string.Equals(entry.Build, candidate.Build, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.Map, candidate.Map, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return FocusGates.Duplicate;
        }

        FocusStrength strength = player.Strength;
        if (strength?.IsKnown == true)
        {
            if (strength.Games < settings.MinHeroGames)
            {
                return FocusGates.StrengthGames;
            }

            if ((strength.WinRate ?? 0) < settings.MinWinRate)
            {
                return FocusGates.StrengthWinRate;
            }
        }
        else if (!settings.AllowUnknownStrength)
        {
            return FocusGates.StrengthUnknown;
        }

        return quality < settings.QualityFloor ? FocusGates.QualityFloor : null;
    }

    private double Quality(
        FocusCard card,
        FocusCardPlayer player,
        HeroSignature signature,
        HeroBaselines baselines,
        Dictionary<string, double> parts
    )
    {
        PerformanceWeights pw = settings.Weights?.Performance ?? new PerformanceWeights();
        double signatureScore = SignatureScore(card, player, signature, baselines);
        double kda = Kda(player, signature);
        double participation = Participation(card, player);
        double awards = Awards(player, signature);
        double won = player.Won ? 1 : 0;
        double performance = Weighted(
            (pw.Signature, signatureScore),
            (pw.Kda, kda),
            (pw.Participation, participation),
            (pw.Awards, awards),
            (pw.Won, won)
        );

        QualityWeights qw = settings.Weights?.Quality ?? new QualityWeights();
        double strength = Strength(player.Strength);
        double build = BuildClarity(player.Build);
        double game = Game(card);

        parts["signature"] = Round(signatureScore);
        parts["kda"] = Round(kda);
        parts["participation"] = Round(participation);
        parts["awards"] = Round(awards);
        parts["won"] = won;
        parts["performance"] = Round(performance);
        parts["strength"] = Round(strength);
        parts["build"] = Round(build);
        parts["game"] = Round(game);
        return Weighted(
            (qw.Performance, performance),
            (qw.Strength, strength),
            (qw.Build, build),
            (qw.Game, game)
        );
    }

    /// <summary>
    /// The mean of the three best signature percentiles: each stat against the hero's baseline,
    /// else its role's, else the other players in this match.
    /// </summary>
    private double SignatureScore(
        FocusCard card,
        FocusCardPlayer player,
        HeroSignature signature,
        HeroBaselines baselines
    )
    {
        var percentiles = new List<double>();
        foreach (string stat in signature.Stats)
        {
            if (player.Stats != null && player.Stats.TryGetValue(stat, out double value))
            {
                percentiles.Add(Percentile(card, player, signature, baselines, stat, value, false));
            }
        }

        foreach (string stat in signature.Low)
        {
            if (signature.IgnoreDeaths && DeathStats.Contains(stat))
            {
                continue;
            }

            if (player.Stats != null && player.Stats.TryGetValue(stat, out double value))
            {
                percentiles.Add(Percentile(card, player, signature, baselines, stat, value, true));
            }
        }

        return percentiles.Count == 0
            ? 0
            : percentiles.OrderByDescending(value => value).Take(3).Average();
    }

    private double Percentile(
        FocusCard card,
        FocusCardPlayer player,
        HeroSignature signature,
        HeroBaselines baselines,
        string stat,
        double value,
        bool lowerIsBetter
    ) =>
        baselines.Percentile(
            player.AttributeId,
            signature.Role,
            stat,
            value,
            lowerIsBetter,
            Math.Max(1, settings.BaselineMinSamples)
        ) ?? MatchPercentile(card, stat, value, lowerIsBetter);

    /// <summary>Where the value falls among this match's players (ties count half).</summary>
    private static double MatchPercentile(
        FocusCard card,
        string stat,
        double value,
        bool lowerIsBetter
    )
    {
        double[] values = card
            .Players.Where(other => other.Stats != null && other.Stats.ContainsKey(stat))
            .Select(other => other.Stats[stat])
            .ToArray();
        if (values.Length == 0)
        {
            return 0.5;
        }

        double below = values.Count(seen => seen < value);
        double same = values.Count(seen => seen == value);
        double percentile = (below + same / 2) / values.Length;
        return lowerIsBetter ? 1 - percentile : percentile;
    }

    private double Kda(FocusCardPlayer player, HeroSignature signature)
    {
        double deaths = signature.IgnoreDeaths ? 0 : player.Stat(nameof(ScoreResult.Deaths));
        double takedowns =
            player.Stat(nameof(ScoreResult.SoloKills)) + player.Stat(nameof(ScoreResult.Assists));
        double ratio = takedowns / Math.Max(1, deaths);
        return Math.Clamp(ratio / Math.Max(1, settings.KdaCap), 0, 1);
    }

    /// <summary>The player's takedowns over their team's kills.</summary>
    private static double Participation(FocusCard card, FocusCardPlayer player)
    {
        double teamKills = card
            .Players.Where(other => other.Team == player.Team)
            .Sum(other => other.Stat(nameof(ScoreResult.SoloKills)));
        return teamKills <= 0
            ? 0
            : Math.Clamp(player.Stat(nameof(ScoreResult.Takedowns)) / teamKills, 0, 1);
    }

    /// <summary>MVP is half; each signature award a quarter; any other award a tenth.</summary>
    private static double Awards(FocusCardPlayer player, HeroSignature signature)
    {
        double total = 0;
        foreach (string award in player.Awards ?? new List<string>())
        {
            if (string.Equals(award, nameof(MatchAwardType.MVP), StringComparison.Ordinal))
            {
                total += 0.5;
            }
            else if (signature.Awards.Contains(award, StringComparer.Ordinal))
            {
                total += 0.25;
            }
            else
            {
                total += 0.1;
            }
        }

        return Math.Min(1, total);
    }

    /// <summary>
    /// The Wilson lower bound of the player's win rate on the hero, 45% scoring 0 and 60% scoring
    /// 1. An unknown record counts as <see cref="HeroFocusSettings.UnknownStrength"/>.
    /// </summary>
    private double Strength(FocusStrength strength)
    {
        if (strength?.IsKnown != true || strength.Games <= 0)
        {
            return Math.Clamp(settings.UnknownStrength, 0, 1);
        }

        double lower = HeroStatsMath.WilsonLower(strength.Wins, strength.Games) / 100.0;
        return Math.Clamp((lower - 0.45) / 0.15, 0, 1);
    }

    /// <summary>A clear guide build is 1, a close one 0.6, an off-guide or unknown one 0.2.</summary>
    public static double BuildClarity(FocusCardBuild build)
    {
        if (build == null)
        {
            return 0.2;
        }

        if (build.Score >= 0.85 && !build.Ambiguous)
        {
            return 1;
        }

        return build.Score >= 0.70 ? 0.6 : 0.2;
    }

    private double Game(FocusCard card)
    {
        double minutes = card.LengthSeconds / 60;
        if (card.LengthSeconds < settings.ShortGame.TotalSeconds)
        {
            return 0.3;
        }

        return minutes <= 30 ? 1 : 0.8;
    }

    private double Novelty(
        FocusCard card,
        FocusCandidate candidate,
        History history,
        DateTime nowUtc,
        Dictionary<string, double> parts
    )
    {
        NoveltyWeights nw = settings.Weights?.Novelty ?? new NoveltyWeights();
        double hero = HeroNovelty(candidate, history, nowUtc);
        double role = Deficit(
            candidate.Role,
            history.Recent.Select(entry => entry.Role).ToList(),
            RoleCount
        );
        double build = BuildNovelty(candidate, history);
        double map = Deficit(card.Map, history.Recent.Select(entry => entry.Map).ToList());
        double league = Deficit(
            candidate.League,
            history.Recent.Select(entry => entry.League).ToList()
        );
        double region = Deficit(card.Region, history.Recent.Select(entry => entry.Region).ToList());
        double comp = history.All.Any(entry =>
            SameHero(entry, candidate)
            && string.Equals(entry.EnemyComp, candidate.EnemyComp, StringComparison.Ordinal)
        )
            ? 0.3
            : 1;

        parts["novelty.hero"] = Round(hero);
        parts["novelty.role"] = Round(role);
        parts["novelty.build"] = Round(build);
        parts["novelty.map"] = Round(map);
        parts["novelty.league"] = Round(league);
        parts["novelty.region"] = Round(region);
        parts["novelty.comp"] = Round(comp);
        return Weighted(
            (nw.Hero, hero),
            (nw.Role, role),
            (nw.Build, build),
            (nw.Map, map),
            (nw.League, league),
            (nw.Region, region),
            (nw.Comp, comp)
        );
    }

    /// <summary>
    /// 1 for a hero never covered. Otherwise <c>1 / (1 + coverage)</c>, rising back to that from
    /// 0 over <see cref="HeroFocusSettings.HeroRecovery"/> after the hero's last focus. A
    /// published focus covers fully, one only played covers half.
    /// </summary>
    private double HeroNovelty(FocusCandidate candidate, History history, DateTime nowUtc)
    {
        List<FocusLedgerEntry> covered = history
            .All.Where(entry => SameHero(entry, candidate))
            .ToList();
        if (covered.Count == 0)
        {
            return 1;
        }

        double coverage = covered.Sum(entry =>
            entry.ReplayId is int id && history.Published.Contains(id) ? 1.0 : 0.5
        );
        double days = (nowUtc - covered.Max(entry => entry.PickedAtUtc)).TotalDays;
        double recovery =
            settings.HeroRecovery <= TimeSpan.Zero
                ? 1
                : Math.Clamp(days / settings.HeroRecovery.TotalDays, 0, 1);
        return recovery / (1 + coverage);
    }

    private static double BuildNovelty(FocusCandidate candidate, History history)
    {
        if (candidate.Build == null)
        {
            return 0.3;
        }

        int times = history.All.Count(entry =>
            SameHero(entry, candidate)
            && string.Equals(entry.Build, candidate.Build, StringComparison.OrdinalIgnoreCase)
        );
        return 1.0 / (1 + times);
    }

    /// <summary>
    /// How far a value is below its fair share of the recent picks: 1 when it is missing from
    /// them, 0.5 at exactly its share, 0 at twice it. The share is one over the values seen
    /// (or <paramref name="categories"/> when known). An unknown value is 0.5.
    /// </summary>
    public static double Deficit(string value, IReadOnlyList<string> recent, int categories = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0.5;
        }

        List<string> known = (recent ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
        if (known.Count == 0)
        {
            return 1;
        }

        int kinds =
            categories > 0
                ? categories
                : known.Append(value).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        double target = 1.0 / Math.Max(1, kinds);
        double share =
            (double)
                known.Count(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
            / known.Count;
        return Math.Clamp(0.5 + 0.5 * (target - share) / target, 0, 1);
    }

    /// <summary>The composition labels as one key, in order: <c>Dive+Sustain</c>, or <c>none</c>.</summary>
    public static string CompKey(IEnumerable<string> labels)
    {
        string[] sorted = (labels ?? Enumerable.Empty<string>())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(label => label, StringComparer.Ordinal)
            .ToArray();
        return sorted.Length == 0 ? "none" : string.Join("+", sorted);
    }

    private static bool SameHero(FocusLedgerEntry entry, FocusCandidate candidate) =>
        !string.IsNullOrWhiteSpace(candidate.AttributeId)
        && string.Equals(
            entry.AttributeId,
            candidate.AttributeId,
            StringComparison.OrdinalIgnoreCase
        );

    private static double Weighted(params (double Weight, double Value)[] parts)
    {
        double weights = parts.Sum(part => Math.Max(0, part.Weight));
        return weights <= 0
            ? 0
            : parts.Sum(part => Math.Max(0, part.Weight) * Math.Clamp(part.Value, 0, 1)) / weights;
    }

    private static double Round(double value) => Math.Round(value, 4);

    private sealed record History(
        IReadOnlyList<FocusLedgerEntry> All,
        IReadOnlyList<FocusLedgerEntry> Recent,
        IReadOnlySet<string> Repeat,
        IReadOnlySet<string> Used,
        IReadOnlySet<int> Published
    );
}
