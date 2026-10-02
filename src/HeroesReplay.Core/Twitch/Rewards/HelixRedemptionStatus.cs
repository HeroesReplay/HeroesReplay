using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Twitch.Rewards;

public enum RedemptionUpdate
{
    /// <summary>Twitch now shows the new status.</summary>
    Updated,

    /// <summary>Twitch refused this redemption for good: not found, not ours, or no longer unfulfilled.</summary>
    Refused,

    /// <summary>A network error, a rate limit, or a Twitch error. Try again later.</summary>
    Retry,

    /// <summary>The Twitch client id or token is missing.</summary>
    NotConfigured,
}

public interface IRedemptionStatusClient
{
    Task<RedemptionUpdate> UpdateAsync(
        string broadcasterId,
        Guid rewardId,
        Guid redemptionId,
        string status,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Helix <c>PATCH channel_points/custom_rewards/redemptions</c>. Twitch only changes a redemption
/// that is still UNFULFILLED, of a reward created with this client id.
/// </summary>
public sealed class HelixRedemptionStatus : IRedemptionStatusClient
{
    private const string Endpoint =
        "https://api.twitch.tv/helix/channel_points/custom_rewards/redemptions";

    private readonly ILogger<HelixRedemptionStatus> logger;
    private readonly AppSettings settings;

    public HelixRedemptionStatus(ILogger<HelixRedemptionStatus> logger, AppSettings settings)
    {
        this.logger = logger;
        this.settings = settings;
    }

    public async Task<RedemptionUpdate> UpdateAsync(
        string broadcasterId,
        Guid rewardId,
        Guid redemptionId,
        string status,
        CancellationToken cancellationToken
    )
    {
        if (
            string.IsNullOrWhiteSpace(settings.Twitch?.AccessToken)
            || string.IsNullOrWhiteSpace(settings.Twitch?.ClientId)
        )
        {
            return RedemptionUpdate.NotConfigured;
        }

        if (
            string.IsNullOrWhiteSpace(broadcasterId)
            || rewardId == Guid.Empty
            || redemptionId == Guid.Empty
            || string.IsNullOrWhiteSpace(status)
        )
        {
            return RedemptionUpdate.Refused;
        }

        string url =
            Endpoint
            + $"?broadcaster_id={Uri.EscapeDataString(broadcasterId)}"
            + $"&reward_id={rewardId:D}&id={redemptionId:D}";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var request = new HttpRequestMessage(HttpMethod.Patch, url);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                settings.Twitch.AccessToken
            );
            request.Headers.TryAddWithoutValidation("Client-Id", settings.Twitch.ClientId);
            request.Content = new StringContent(
                "{\"status\":\"" + status + "\"}",
                Encoding.UTF8,
                "application/json"
            );
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            RedemptionUpdate result = Classify(response.StatusCode);
            if (result != RedemptionUpdate.Updated)
            {
                logger.LogWarning(
                    "Twitch did not set redemption {RedemptionId} to {Status} ({Code}).",
                    redemptionId,
                    status,
                    (int)response.StatusCode
                );
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(
                "Could not set redemption {RedemptionId} to {Status}: {Error}",
                redemptionId,
                status,
                e.GetType().Name
            );
            return RedemptionUpdate.Retry;
        }
    }

    public static RedemptionUpdate Classify(HttpStatusCode code)
    {
        int value = (int)code;
        if (value >= 200 && value < 300)
        {
            return RedemptionUpdate.Updated;
        }

        if (code == HttpStatusCode.TooManyRequests || code == HttpStatusCode.Unauthorized)
        {
            return RedemptionUpdate.Retry;
        }

        return value >= 400 && value < 500 ? RedemptionUpdate.Refused : RedemptionUpdate.Retry;
    }
}
