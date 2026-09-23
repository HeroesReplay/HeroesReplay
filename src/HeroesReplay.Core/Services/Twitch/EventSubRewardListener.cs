using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Twitch.RedeemedRewards;
using Microsoft.Extensions.Logging;
using TwitchLib.Api.Interfaces;

namespace HeroesReplay.Core.Services.Twitch;

public sealed class EventSubRewardListener : IDisposable
{
    private const string SocketUrl = "wss://eventsub.wss.twitch.tv/ws?keepalive_timeout_seconds=30";

    private readonly ILogger<EventSubRewardListener> logger;
    private readonly AppSettings settings;
    private readonly ITwitchAPI api;
    private readonly IOnRewardHandler rewards;
    private readonly HttpClient http;
    private int listenGate;

    public EventSubRewardListener(
        ILogger<EventSubRewardListener> logger,
        AppSettings settings,
        ITwitchAPI api,
        IOnRewardHandler rewards
    )
    {
        this.logger = logger;
        this.settings = settings;
        this.api = api;
        this.rewards = rewards;
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task ListenAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref listenGate, 1) != 0)
        {
            logger.LogInformation("EventSub reward listener is already running.");
            return;
        }

        int backoffSeconds = 1;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(SocketUrl, subscribe: true, cancellationToken)
                    .ConfigureAwait(false);
                backoffSeconds = 1;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "EventSub reward listener stopped. Reconnecting.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                backoffSeconds = Math.Min(30, backoffSeconds * 2);
            }
        }
    }

    private async Task RunSessionAsync(
        string url,
        bool subscribe,
        CancellationToken cancellationToken
    )
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
        var buffer = new byte[16 * 1024];
        string reconnectUrl = null;
        int keepaliveSeconds = 30;
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(keepaliveSeconds + 10)
            );
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token
            );
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("EventSub keepalive expired.");
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            while (!result.EndOfMessage)
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                message += Encoding.UTF8.GetString(buffer, 0, result.Count);
            }

            if (EventSubRedemption.TryReadWelcome(message, out string sessionId, out int keepalive))
            {
                keepaliveSeconds = keepalive;
                logger.LogInformation("EventSub connected (session {Session}).", sessionId);
                if (subscribe)
                {
                    await SubscribeAsync(sessionId, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            if (EventSubRedemption.TryReadReconnect(message, out reconnectUrl))
            {
                logger.LogInformation("EventSub asked for a reconnect.");
                break;
            }

            if (EventSubRedemption.TryReadReward(message, out var reward))
            {
                logger.LogInformation(
                    "{Login} redeemed {Title}.",
                    reward.Login,
                    reward.RewardTitle
                );
                rewards.Handle(reward);
            }
        }

        if (!string.IsNullOrWhiteSpace(reconnectUrl))
        {
            await RunSessionAsync(reconnectUrl, subscribe: false, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task SubscribeAsync(string sessionId, CancellationToken cancellationToken)
    {
        string channelId = await GetChannelIdAsync().ConfigureAwait(false);
        var body = new
        {
            type = EventSubRedemption.AddType,
            version = "1",
            condition = new { broadcaster_user_id = channelId },
            transport = new { method = "websocket", session_id = sessionId },
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.twitch.tv/helix/eventsub/subscriptions"
        );
        request.Headers.TryAddWithoutValidation("Client-Id", settings.Twitch.ClientId);
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            "Bearer " + settings.Twitch.AccessToken
        );
        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json"
        );
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        string responseText = await response
            .Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "EventSub redemption subscribe failed ("
                    + (int)response.StatusCode
                    + "). "
                    + Trim(responseText)
            );
        }

        logger.LogInformation(
            "Subscribed to channel-point redemptions for channel {ChannelId}.",
            channelId
        );
    }

    private async Task<string> GetChannelIdAsync()
    {
        var users = await api
            .Helix.Users.GetUsersAsync(
                logins: new System.Collections.Generic.List<string> { settings.Twitch.Channel }
            )
            .ConfigureAwait(false);
        if (users?.Users == null || users.Users.Length == 0)
        {
            throw new InvalidOperationException(
                "Helix returned no user for " + settings.Twitch.Channel + "."
            );
        }

        return users.Users[0].Id;
    }

    private static string Trim(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "no response body";
        }

        text = text.Replace('\n', ' ').Replace('\r', ' ');
        return text.Length <= 300 ? text : text.Substring(0, 300);
    }

    public void Dispose()
    {
        http.Dispose();
    }
}
