using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;

namespace HeroesReplay.Core.HeroFocus.Cards;

/// <summary>
/// A player's end-of-game score screen as named numbers (#433). Each name is the
/// <see cref="ScoreResult"/> property it comes from, so a signature that names a stat the parser
/// does not have fails a test, not a game. Times are in seconds. A stat the replay left empty is
/// missing, not zero.
/// </summary>
public static class ScoreStats
{
    private static readonly (string Name, Func<ScoreResult, double?> Read)[] Fields =
    {
        (nameof(ScoreResult.Level), score => score.Level),
        (nameof(ScoreResult.Takedowns), score => score.Takedowns),
        (nameof(ScoreResult.SoloKills), score => score.SoloKills),
        (nameof(ScoreResult.Assists), score => score.Assists),
        (nameof(ScoreResult.Deaths), score => score.Deaths),
        (nameof(ScoreResult.HeroDamage), score => score.HeroDamage),
        (nameof(ScoreResult.SiegeDamage), score => score.SiegeDamage),
        (nameof(ScoreResult.StructureDamage), score => score.StructureDamage),
        (nameof(ScoreResult.MinionDamage), score => score.MinionDamage),
        (nameof(ScoreResult.CreepDamage), score => score.CreepDamage),
        (nameof(ScoreResult.SummonDamage), score => score.SummonDamage),
        (nameof(ScoreResult.TimeCCdEnemyHeroes), score => score.TimeCCdEnemyHeroes?.TotalSeconds),
        (nameof(ScoreResult.Healing), score => score.Healing),
        (nameof(ScoreResult.SelfHealing), score => score.SelfHealing),
        (nameof(ScoreResult.RegenGlobes), score => score.RegenGlobes),
        (nameof(ScoreResult.DamageTaken), score => score.DamageTaken),
        (nameof(ScoreResult.DamageSoaked), score => score.DamageSoaked),
        (nameof(ScoreResult.ExperienceContribution), score => score.ExperienceContribution),
        (nameof(ScoreResult.TownKills), score => score.TownKills),
        (nameof(ScoreResult.TimeSpentDead), score => score.TimeSpentDead.TotalSeconds),
        (nameof(ScoreResult.MercCampCaptures), score => score.MercCampCaptures),
        (nameof(ScoreResult.WatchTowerCaptures), score => score.WatchTowerCaptures),
        (nameof(ScoreResult.MetaExperience), score => score.MetaExperience),
        (nameof(ScoreResult.HighestKillStreak), score => score.HighestKillStreak),
        (nameof(ScoreResult.ProtectionGivenToAllies), score => score.ProtectionGivenToAllies),
        (nameof(ScoreResult.TimeSilencingEnemyHeroes), score => score.TimeSilencingEnemyHeroes),
        (nameof(ScoreResult.TimeRootingEnemyHeroes), score => score.TimeRootingEnemyHeroes),
        (nameof(ScoreResult.TimeStunningEnemyHeroes), score => score.TimeStunningEnemyHeroes),
        (nameof(ScoreResult.ClutchHealsPerformed), score => score.ClutchHealsPerformed),
        (nameof(ScoreResult.EscapesPerformed), score => score.EscapesPerformed),
        (nameof(ScoreResult.VengeancesPerformed), score => score.VengeancesPerformed),
        (nameof(ScoreResult.OutnumberedDeaths), score => score.OutnumberedDeaths),
        (nameof(ScoreResult.TeamfightEscapesPerformed), score => score.TeamfightEscapesPerformed),
        (nameof(ScoreResult.TeamfightHealingDone), score => score.TeamfightHealingDone),
        (nameof(ScoreResult.TeamfightDamageTaken), score => score.TeamfightDamageTaken),
        (nameof(ScoreResult.TeamfightHeroDamage), score => score.TeamfightHeroDamage),
        (nameof(ScoreResult.Multikill), score => score.Multikill),
        (nameof(ScoreResult.PhysicalDamage), score => score.PhysicalDamage),
        (nameof(ScoreResult.SpellDamage), score => score.SpellDamage),
        (nameof(ScoreResult.OnFireTimeonFire), score => score.OnFireTimeonFire?.TotalSeconds),
    };

    private static readonly HashSet<string> Known = new(
        Fields.Select(field => field.Name),
        StringComparer.Ordinal
    );

    /// <summary>Every stat name, in score-screen order.</summary>
    public static IReadOnlyList<string> Names { get; } =
        Fields.Select(field => field.Name).ToArray();

    public static bool IsStat(string name) => name != null && Known.Contains(name);

    public static bool IsAward(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && Enum.TryParse(name, ignoreCase: false, out MatchAwardType _)
        && !int.TryParse(name, out _);

    /// <summary>The player's stats. Empty when the replay has no score screen for them.</summary>
    public static Dictionary<string, double> Read(ScoreResult score)
    {
        var stats = new Dictionary<string, double>(StringComparer.Ordinal);
        if (score == null)
        {
            return stats;
        }

        foreach ((string name, Func<ScoreResult, double?> read) in Fields)
        {
            if (read(score) is double value)
            {
                stats[name] = value;
            }
        }

        return stats;
    }

    /// <summary>The player's end-of-game awards, by <see cref="MatchAwardType"/> name.</summary>
    public static List<string> Awards(ScoreResult score) =>
        (score?.MatchAwards ?? new List<MatchAwardType>())
            .Select(award => award.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
