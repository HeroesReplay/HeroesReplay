using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Twitch.Rewards;

public interface IRedemptionCanceller
{
    Task<bool> TryCancelAsync(string broadcasterId, Guid rewardId, Guid redemptionId);
}

public sealed class RedemptionCanceller : IRedemptionCanceller
{
    private readonly ILogger<RedemptionCanceller> logger;
    private readonly AppSettings settings;

    public RedemptionCanceller(ILogger<RedemptionCanceller> logger, AppSettings settings)
    {
        this.logger = logger;
        this.settings = settings;
    }

    public async Task<bool> TryCancelAsync(string broadcasterId, Guid rewardId, Guid redemptionId)
    {
        if (
            string.IsNullOrWhiteSpace(broadcasterId)
            || rewardId == Guid.Empty
            || redemptionId == Guid.Empty
            || string.IsNullOrWhiteSpace(settings.Twitch?.AccessToken)
            || string.IsNullOrWhiteSpace(settings.Twitch?.ClientId)
        )
        {
            return false;
        }

        string url =
            "https://api.twitch.tv/helix/channel_points/custom_rewards/redemptions"
            + $"?broadcaster_id={Uri.EscapeDataString(broadcasterId)}"
            + $"&reward_id={rewardId:D}&id={redemptionId:D}";
        try
        {
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Patch, url);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                settings.Twitch.AccessToken
            );
            request.Headers.TryAddWithoutValidation("Client-Id", settings.Twitch.ClientId);
            request.Content = new StringContent(
                "{\"status\":\"CANCELED\"}",
                Encoding.UTF8,
                "application/json"
            );
            using HttpResponseMessage response = await http.SendAsync(request)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Could not return channel points for redemption {RedemptionId} ({Status}).",
                    redemptionId,
                    (int)response.StatusCode
                );
                return false;
            }

            return true;
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Could not return channel points for redemption {RedemptionId}.",
                redemptionId
            );
            return false;
        }
    }
}
