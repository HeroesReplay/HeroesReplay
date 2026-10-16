using System;
using Heroes.ReplayParser;

namespace HeroesReplay.Core.HeroFocus.Cards;

/// <summary>
/// How a focus card's replay is parsed (#433): events, units and statistics for the score screen
/// and the talents, and the detailed battle lobby for each player's BattleTag number. Mouse and
/// chat events are left out.
/// </summary>
public static class FocusReplayParse
{
    public static readonly ParseOptions Options = new()
    {
        IgnoreErrors = true,
        AllowPTR = true,
        ShouldParseEvents = true,
        ShouldParseUnits = true,
        ShouldParseStatistics = true,
        ShouldParseDetailedBattleLobby = true,
        ShouldParseMouseEvents = false,
        ShouldParseMessageEvents = false,
    };

    /// <summary>The parsed replay, or null with the parser's result when it did not parse.</summary>
    public static (DataParser.ReplayParseResult Result, Replay Replay) Read(string path)
    {
        try
        {
            (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(
                path,
                false,
                Options
            );
            return replay?.Players == null || replay.Players.Length == 0
                ? (result, null)
                : (result, replay);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return (DataParser.ReplayParseResult.Exception, null);
        }
    }
}
