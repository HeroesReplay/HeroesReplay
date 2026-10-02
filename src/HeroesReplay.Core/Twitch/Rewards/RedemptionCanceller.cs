using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Twitch.Rewards;

public interface IRedemptionCanceller
{
    Task<bool> TryCancelAsync(string broadcasterId, Guid rewardId, Guid redemptionId);
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
        RedemptionUpdate result = await status
            .UpdateAsync(
                broadcasterId,
                rewardId,
                redemptionId,
                RewardRedemptionStatus.Canceled,
                CancellationToken.None
            )
            .ConfigureAwait(false);
        return result == RedemptionUpdate.Updated;
    }
}
