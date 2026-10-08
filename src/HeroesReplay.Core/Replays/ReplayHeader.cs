using System;
using System.IO;
using Heroes.ReplayParser;

namespace HeroesReplay.Core.Replays;

/// <summary>
/// A replay's details without events, units, or statistics: the game date, build, map, and
/// players. Cheap enough for the downloader to judge every waiting file (#280).
/// </summary>
public static class ReplayHeader
{
    private static readonly ParseOptions Options = new()
    {
        AllowPTR = false,
        IgnoreErrors = false,
        ShouldParseDetailedBattleLobby = false,
        ShouldParseEvents = false,
        ShouldParseMouseEvents = false,
        ShouldParseStatistics = false,
        ShouldParseUnits = false,
        ShouldParseMessageEvents = false,
    };

    /// <summary>Null when the file cannot be read or parsed.</summary>
    public static Replay Load(string path)
    {
        try
        {
            (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(
                File.ReadAllBytes(path),
                Options
            );
            return
                result == DataParser.ReplayParseResult.Success
                || result == DataParser.ReplayParseResult.UnexpectedResult
                ? replay
                : null;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }
}
