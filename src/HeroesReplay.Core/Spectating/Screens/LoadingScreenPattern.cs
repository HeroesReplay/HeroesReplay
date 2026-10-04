using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// Finds the client's screen-state global. The client tests it the same way in many places:
/// `mov rcx,[G]; test rcx,rcx; jz; xor edx,edx; call; test al,al; jz; mov rcx,[G]; call`.
/// Both loads must name the same global. The global with the most such sites wins, and only
/// when no other global comes close. On 2.57.0.98304 that is RVA 0x3771830, with 34 sites.
/// </summary>
public static class LoadingScreenPattern
{
    public const int Width = 31;
    public const int MinimumSites = 3;
    private const int FirstDisplacement = 3;
    private const int FirstLoadEnd = 7;
    private const int SecondDisplacement = 26;
    private const int SecondLoadEnd = 30;

    public static List<long> Find(ReadOnlySpan<byte> bytes, long byteRva)
    {
        var globals = new List<long>();
        for (int i = 0; i + Width <= bytes.Length; i++)
        {
            if (!Matches(bytes, i))
            {
                continue;
            }

            long site = byteRva + i;
            long first =
                site + FirstLoadEnd + BitConverter.ToInt32(bytes.Slice(i + FirstDisplacement, 4));
            long second =
                site + SecondLoadEnd + BitConverter.ToInt32(bytes.Slice(i + SecondDisplacement, 4));
            if (first == second && first > 0)
            {
                globals.Add(first);
            }
        }

        return globals;
    }

    /// <summary>
    /// The winner needs <see cref="MinimumSites"/> sites and at least twice the runner-up.
    /// </summary>
    public static bool TryAgree(IReadOnlyList<long> globals, out long globalRva, out int sites)
    {
        globalRva = 0;
        sites = 0;
        if (globals == null || globals.Count == 0)
        {
            return false;
        }

        var ranked = globals
            .GroupBy(global => global)
            .Select(group => (Rva: group.Key, Count: group.Count()))
            .OrderByDescending(group => group.Count)
            .ToList();
        int runnerUp = ranked.Count > 1 ? ranked[1].Count : 0;
        if (ranked[0].Count < MinimumSites || ranked[0].Count < runnerUp * 2)
        {
            return false;
        }

        globalRva = ranked[0].Rva;
        sites = ranked[0].Count;
        return true;
    }

    private static bool Matches(ReadOnlySpan<byte> b, int i)
    {
        return b[i] == 0x48
            && b[i + 1] == 0x8B
            && b[i + 2] == 0x0D
            && b[i + 7] == 0x48
            && b[i + 8] == 0x85
            && b[i + 9] == 0xC9
            && b[i + 10] == 0x74
            && b[i + 12] == 0x33
            && b[i + 13] == 0xD2
            && b[i + 14] == 0xE8
            && b[i + 19] == 0x84
            && b[i + 20] == 0xC0
            && b[i + 21] == 0x74
            && b[i + 23] == 0x48
            && b[i + 24] == 0x8B
            && b[i + 25] == 0x0D
            && b[i + 30] == 0xE8;
    }
}
