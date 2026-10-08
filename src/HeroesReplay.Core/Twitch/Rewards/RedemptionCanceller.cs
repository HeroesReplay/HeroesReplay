using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Twitch.Rewards;

/// <summary>
/// Sets a redemption CANCELED on Twitch, which returns the viewer's channel points. Only the
/// twitch role (<c>twitch connect</c>) calls it.
/// </summary>
public interface IRedemptionCanceller
{
    Task<bool> TryCancelAsync(string broadcasterId, Guid rewardId, Guid redemptionId);

    /// <summary>
    /// What Twitch answered, so a caller can tell a refusal (do not send again) from a network
    /// error (send again later).
    /// </summary>
    Task<RedemptionUpdate> CancelAsync(
        string broadcasterId,
        Guid rewardId,
        Guid redemptionId,
        CancellationToken cancellationToken
    );
}

public sealed class RedemptionCanceller : IRedemptionCanceller
{
    private readonly IRedemptionStatusClient status;

    public RedemptionCanceller(IRedemptionStatusClient status)
    {
        this.status = status;
    }

    public async Task<bool> TryCancelAsync(string broadcasterId, Guid rewardId, Guid redemptionId)
    {
        RedemptionUpdate result = await CancelAsync(
                broadcasterId,
                rewardId,
                redemptionId,
                CancellationToken.None
            )
            .ConfigureAwait(false);
        return result == RedemptionUpdate.Updated;
    }

    public Task<RedemptionUpdate> CancelAsync(
        string broadcasterId,
        Guid rewardId,
        Guid redemptionId,
        CancellationToken cancellationToken
    ) =>
        status.UpdateAsync(
            broadcasterId,
            rewardId,
            redemptionId,
            RewardRedemptionStatus.Canceled,
            cancellationToken
        );
}
