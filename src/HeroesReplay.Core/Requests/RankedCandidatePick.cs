using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Requests;

/// <summary>What a rank reward's search found (#429). <see cref="Replay"/> is null when none matched.</summary>
public sealed record RankedPick(HeroesProfileReplay Replay, string Tier, int Looked, bool TimedOut);

/// <summary>
/// A rank reward's pick (#429). Heroes Profile's replay list has no rank or MMR filter, so the
/// candidates are shuffled and looked up one at a time, and the first whose tier is in the
/// entered league is queued. At most <c>limit</c> are looked up: each lookup is a Heroes Profile
/// call, and <c>/mmr/tier</c> allows 60 a minute.
/// </summary>
public static class RankedCandidatePick
{
    public static async Task<RankedPick> FindAsync(
        IEnumerable<HeroesProfileReplay> candidates,
        GameRank rank,
        Func<HeroesProfileReplay, CancellationToken, Task<string>> tierOf,
        int limit,
        Random random,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(tierOf);
        ArgumentNullException.ThrowIfNull(random);
        HeroesProfileReplay[] order = (candidates ?? Enumerable.Empty<HeroesProfileReplay>())
            .Where(replay => replay != null)
            .ToArray();
        random.Shuffle(order);
        int looked = 0;
        try
        {
            foreach (HeroesProfileReplay candidate in order.Take(Math.Max(0, limit)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                looked++;
                string tier = await tierOf(candidate, cancellationToken).ConfigureAwait(false);
                if (RankInput.Read(tier) == rank)
                {
                    return new RankedPick(candidate, tier.Trim(), looked, TimedOut: false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RankedPick(null, null, looked, TimedOut: true);
        }

        return new RankedPick(null, null, looked, TimedOut: false);
    }
}
