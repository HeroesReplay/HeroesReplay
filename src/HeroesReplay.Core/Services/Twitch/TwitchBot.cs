using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Shared;
using HeroesReplay.Core.Services.Twitch.ChatMessages;
using HeroesReplay.Core.Services.Twitch.RedeemedRewards;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Events;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;

namespace HeroesReplay.Core.Services.Twitch;

public class TwitchBot : ITwitchBot
{
    private readonly ITwitchClient client;
    private readonly AppSettings settings;
    private readonly ILogger<TwitchBot> logger;
    private readonly IOnMessageHandler onMessageHandler;
    private readonly EventSubRewardListener rewards;
    private readonly CancellationTokenProvider tokenProvider;
    private int chatBackoffSeconds = 1;
    private bool chatReconnecting;
    private readonly object reconnectLock = new object();

    public TwitchBot(
        ILogger<TwitchBot> logger,
        AppSettings settings,
        ITwitchClient client,
        IOnMessageHandler onMessageHandler,
        EventSubRewardListener rewards,
        CancellationTokenProvider tokenProvider
    )
    {
        this.logger = logger;
        this.settings = settings;
        this.client = client;
        this.onMessageHandler = onMessageHandler;
        this.rewards = rewards;
        this.tokenProvider = tokenProvider;
    }

    public Task InitializeAsync()
    {
        if (settings.Twitch.EnableChatBot)
        {
            client.OnLog += Client_OnLog;
            client.OnMessageReceived += Client_OnMessageReceived;
            client.OnConnected += Client_OnConnected;
            client.OnDisconnected += Client_OnDisconnected;
            client.OnConnectionError += Client_OnConnectionError;
            client.OnJoinedChannel += Client_OnJoinedChannel;
            client.OnReconnected += Client_OnReconnected;
            client.Initialize(
                new ConnectionCredentials(
                    settings.Twitch.Account,
                    settings.Twitch.AccessToken,
                    TwitchChatEndpoint.SecureWebSocket
                ),
                settings.Twitch.Channel
            );
            client.Connect();
        }

        if (settings.Twitch.EnablePubSub || settings.Twitch.EnableRequests)
        {
            logger.LogInformation(
                "Listening for channel-point redemptions on EventSub. PubSub rewards were shut down."
            );
            _ = rewards.ListenAsync(tokenProvider.Token);
        }

        return Task.CompletedTask;
    }

    private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
    {
        onMessageHandler.Handle(e);
    }

    private void Client_OnConnectionError(object sender, OnConnectionErrorArgs e)
    {
        logger.LogError(e.Error.Message, "OnConnectionError");
        _ = ReconnectChatAsync();
    }

    private void Client_OnDisconnected(object sender, OnDisconnectedEventArgs e)
    {
        logger.LogWarning("Twitch chat disconnected. Reconnecting.");
        _ = ReconnectChatAsync();
    }

    private void Client_OnReconnected(object sender, OnReconnectedEventArgs e)
    {
        chatBackoffSeconds = 1;
        logger.LogInformation("Twitch chat reconnected.");
        EnsureJoined();
    }

    private async Task ReconnectChatAsync()
    {
        lock (reconnectLock)
        {
            if (chatReconnecting || !settings.Twitch.EnableChatBot)
            {
                return;
            }

            chatReconnecting = true;
        }

        try
        {
            int delay = chatBackoffSeconds;
            chatBackoffSeconds = Math.Min(30, chatBackoffSeconds * 2);
            await Task.Delay(TimeSpan.FromSeconds(delay), tokenProvider.Token)
                .ConfigureAwait(false);
            if (!client.IsConnected)
            {
                client.Connect();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            logger.LogWarning(e, "Twitch chat reconnect failed.");
        }
        finally
        {
            lock (reconnectLock)
            {
                chatReconnecting = false;
            }
        }
    }

    private void EnsureJoined()
    {
        if (string.IsNullOrWhiteSpace(settings.Twitch.Channel) || !client.IsConnected)
        {
            return;
        }

        try
        {
            client.JoinChannel(settings.Twitch.Channel);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not join {Channel}.", settings.Twitch.Channel);
        }
    }

    private void Client_OnLog(object sender, TwitchLib.Client.Events.OnLogArgs e)
    {
        logger.LogDebug($"{e.DateTime}: {e.BotUsername} - {e.Data}");
    }

    private void Client_OnConnected(object sender, OnConnectedArgs e)
    {
        chatBackoffSeconds = 1;
        logger.LogInformation("Twitch chat connected ({Channel}).", e.AutoJoinChannel);
        EnsureJoined();
    }

    private void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
    {
        logger.LogInformation($"{e.BotUsername} joined {e.Channel}");
    }
}
