using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.HeroesProfile.Interest;

/// <summary>
/// A listed replay's score screen from Heroes Profile before it is downloaded (#510), so the
/// download role can take the most interesting of a page's candidates.
/// </summary>
public interface IReplayDetailLookup
{
    /// <summary>
    /// The players' score lines and the game length of <paramref name="replayId"/> from
    /// <c>/replay/{id}</c>, or null when Heroes Profile has none or the call failed.
    /// </summary>
    Task<ReplayDetail> GetDetailAsync(int replayId, CancellationToken cancellationToken);
}
