using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// A replay's league tier, from its players' average MMR on its own game type's ladder (#429).
/// A rank reward compares it with the rank the viewer entered.
/// </summary>
public interface IReplayTierLookup
{
    /// <summary>
    /// The tier of <paramref name="replayId"/> on the <paramref name="gameType"/> ladder, as
    /// Heroes Profile names it ("Gold 3", "Master"). Null when Heroes Profile has no MMR or tier
    /// for it, or the lookup failed.
    /// </summary>
    Task<string> GetTierAsync(int replayId, GameType gameType, CancellationToken cancellationToken);
}
