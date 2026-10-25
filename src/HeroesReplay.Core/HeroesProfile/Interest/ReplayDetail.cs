using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.HeroesProfile.Client.Replay.Item;
using Microsoft.Kiota.Abstractions.Serialization;

namespace HeroesReplay.Core.HeroesProfile.Interest;

/// <summary>
/// One player's score screen line (#510): the same values whether Heroes Profile's
/// <c>/replay/{id}</c> gave them before the download or a parsed replay's score screen did.
/// </summary>
/// <param name="Takedowns">Kills plus assists.</param>
/// <param name="HighestKillStreak">The most takedowns without dying.</param>
/// <param name="Multikill">The most takedowns in one quick burst the player took part in; a team wipe gives every hero in it 5.</param>
/// <param name="SecondsOnFire">Seconds the player was on fire.</param>
public sealed record ReplayScoreLine(
    int Team,
    string Hero,
    int Kills,
    int Deaths,
    int Assists,
    int Takedowns,
    int HighestKillStreak,
    int Multikill,
    int SecondsOnFire
);

/// <summary>A replay's players and length, as <see cref="ReplayInterest"/> scores them (#510).</summary>
public sealed record ReplayDetail(
    int ReplayId,
    int? LengthSeconds,
    IReadOnlyList<ReplayScoreLine> Players
)
{
    /// <summary>
    /// Heroes Profile's <c>/replay/{id}</c> answer: <c>players</c> is one array per team of
    /// player objects, each with <c>team</c>, <c>hero.name</c> and a <c>score</c> object
    /// (<c>kills</c>, <c>deaths</c>, <c>assists</c>, <c>takedowns</c>,
    /// <c>highest_kill_streak</c>, <c>multikill</c>, <c>time_on_fire</c> in seconds), and
    /// <c>game_length</c> is in seconds (checked on 66231266, 2026-10-25). Null without players.
    /// </summary>
    public static ReplayDetail From(int replayId, WithReplayGetResponse response)
    {
        var players = new List<ReplayScoreLine>();
        Collect(response?.Players, players);
        return players.Count == 0 ? null : new ReplayDetail(replayId, response.GameLength, players);
    }

    /// <summary>The same values from a parsed replay's score screen (statistics on), for offline reports.</summary>
    public static ReplayDetail FromReplay(
        int replayId,
        Replay replay,
        IReadOnlyList<Shared.Hero> catalog = null
    )
    {
        List<ReplayScoreLine> players = (replay?.Players ?? Array.Empty<Player>())
            .Where(player =>
                player?.ScoreResult != null && player.PlayerType != PlayerType.Spectator
            )
            .Select(player => new ReplayScoreLine(
                player.Team,
                PlayedHero.Name(catalog, replay, player) ?? player.Character,
                player.ScoreResult.SoloKills,
                player.ScoreResult.Deaths,
                player.ScoreResult.Assists,
                player.ScoreResult.Takedowns,
                player.ScoreResult.HighestKillStreak,
                player.ScoreResult.Multikill,
                (int)(player.ScoreResult.OnFireTimeonFire?.TotalSeconds ?? 0)
            ))
            .ToList();
        return players.Count == 0
            ? null
            : new ReplayDetail(replayId, (int)replay.ReplayLength.TotalSeconds, players);
    }

    private static void Collect(UntypedNode node, List<ReplayScoreLine> players)
    {
        if (node is UntypedArray array)
        {
            foreach (UntypedNode child in array.GetValue())
            {
                Collect(child, players);
            }

            return;
        }

        if (node is not UntypedObject obj)
        {
            return;
        }

        IDictionary<string, UntypedNode> fields = obj.GetValue();
        if (
            !fields.TryGetValue("score", out UntypedNode scoreNode)
            || scoreNode is not UntypedObject
        )
        {
            return;
        }

        IDictionary<string, UntypedNode> score = ((UntypedObject)scoreNode).GetValue();
        string hero =
            fields.TryGetValue("hero", out UntypedNode heroNode)
            && heroNode is UntypedObject heroObj
                ? Text(heroObj.GetValue(), "name")
                : null;
        players.Add(
            new ReplayScoreLine(
                Number(fields, "team"),
                hero,
                Number(score, "kills"),
                Number(score, "deaths"),
                Number(score, "assists"),
                Number(score, "takedowns"),
                Number(score, "highest_kill_streak"),
                Number(score, "multikill"),
                Number(score, "time_on_fire")
            )
        );
    }

    private static string Text(IDictionary<string, UntypedNode> fields, string key) =>
        fields.TryGetValue(key, out UntypedNode node) && node is UntypedString text
            ? text.GetValue()
            : null;

    private static int Number(IDictionary<string, UntypedNode> fields, string key)
    {
        if (!fields.TryGetValue(key, out UntypedNode node))
        {
            return 0;
        }

        return node switch
        {
            UntypedInteger integer => integer.GetValue(),
            UntypedLong number => (int)number.GetValue(),
            UntypedDouble number => (int)number.GetValue(),
            UntypedFloat number => (int)number.GetValue(),
            UntypedString text
                when int.TryParse(
                    text.GetValue(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsed
                ) => parsed,
            _ => 0,
        };
    }
}
