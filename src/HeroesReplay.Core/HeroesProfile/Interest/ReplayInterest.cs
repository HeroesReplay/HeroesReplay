using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.MediaPolicy;

namespace HeroesReplay.Core.HeroesProfile.Interest;

/// <summary>
/// A candidate's points and the reasons behind them, for the log, and each reason's signal
/// (<c>deathless-carry</c>, <c>kill-leader</c>, <c>long-streak</c>, <c>on-fire</c>,
/// <c>team-wipe</c>, <c>long-game</c>, <c>short-game</c>, <c>high-rank</c>) for the report.
/// </summary>
public sealed record ReplayInterestScore(
    int Total,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Signals
)
{
    public static readonly ReplayInterestScore None = new(
        0,
        Array.Empty<string>(),
        Array.Empty<string>()
    );

    public override string ToString() =>
        Reasons.Count == 0 ? $"{Total}" : $"{Total} ({string.Join(", ", Reasons)})";
}

/// <summary>
/// How interesting a replay looks from its score screen alone (#510), before it is downloaded:
/// a standout carry, a long takedown streak, a long time on fire, a team wipe, the game's length
/// and its league. Every signal is a fact on the score screen; nothing is estimated. The points
/// only rank one page's candidates against each other.
/// </summary>
public static class ReplayInterest
{
    public static ReplayInterestScore Score(
        ReplayDetail detail,
        string rank,
        ReplayInterestSettings settings
    )
    {
        settings ??= new ReplayInterestSettings();
        if (detail?.Players == null || detail.Players.Count == 0)
        {
            return ReplayInterestScore.None;
        }

        int total = 0;
        var reasons = new List<string>();
        var signals = new List<string>();
        void Add(int points, string signal, string reason)
        {
            if (points == 0)
            {
                return;
            }

            total += points;
            signals.Add(signal);
            reasons.Add(reason);
        }

        ReplayScoreLine carry = Best(
            detail.Players.Where(line =>
                line.Deaths == 0 && line.Kills >= settings.DeathlessCarryKills
            )
        );
        if (carry != null)
        {
            Add(settings.DeathlessCarryPoints, "deathless-carry", "deathless carry " + Line(carry));
        }

        ReplayScoreLine leader = Best(
            detail.Players.Where(line =>
                line.Kills >= settings.KillLeaderKills && line.Deaths <= 1 && line != carry
            )
        );
        if (leader != null)
        {
            Add(settings.KillLeaderPoints, "kill-leader", "kill leader " + Line(leader));
        }

        ReplayScoreLine streak = detail
            .Players.Where(line => line.HighestKillStreak >= settings.LongStreak)
            .OrderByDescending(line => line.HighestKillStreak)
            .FirstOrDefault();
        if (streak != null)
        {
            Add(
                settings.LongStreakPoints,
                "long-streak",
                $"{streak.HighestKillStreak} takedown streak {streak.Hero}"
            );
        }

        ReplayScoreLine fire = detail
            .Players.Where(line => line.SecondsOnFire >= settings.OnFireSeconds)
            .OrderByDescending(line => line.SecondsOnFire)
            .FirstOrDefault();
        if (fire != null)
        {
            Add(settings.OnFirePoints, "on-fire", $"{fire.SecondsOnFire} s on fire {fire.Hero}");
        }

        if (
            settings.TeamWipeMultikill > 0
            && detail.Players.Any(line => line.Multikill >= settings.TeamWipeMultikill)
        )
        {
            Add(settings.TeamWipePoints, "team-wipe", "team wipe");
        }

        if (detail.LengthSeconds is int seconds && seconds > 0)
        {
            TimeSpan length = TimeSpan.FromSeconds(seconds);
            if (length >= settings.LongGame)
            {
                Add(
                    settings.LongGamePoints,
                    "long-game",
                    $"long game {(int)length.TotalMinutes} min"
                );
            }
            else if (length < settings.ShortGame)
            {
                Add(
                    settings.ShortGamePoints,
                    "short-game",
                    $"short game {(int)length.TotalMinutes} min"
                );
            }
        }

        if (
            ReplayMediaRanks.Index(rank) is int league
            && ReplayMediaRanks.Index(settings.HighRank) is int floor
            && league >= floor
        )
        {
            Add(settings.HighRankPoints, "high-rank", rank.Trim());
        }

        return new ReplayInterestScore(total, reasons, signals);
    }

    private static ReplayScoreLine Best(IEnumerable<ReplayScoreLine> lines) =>
        lines.OrderByDescending(line => line.Kills).ThenBy(line => line.Deaths).FirstOrDefault();

    private static string Line(ReplayScoreLine line) =>
        $"{line.Hero} {line.Kills}/{line.Deaths}/{line.Assists}";
}
