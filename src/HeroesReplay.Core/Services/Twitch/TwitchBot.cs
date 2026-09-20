using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Shared;
using HeroesReplay.Core.Services.Twitch.ChatMessages;
using HeroesReplay.Core.Services.Twitch.RedeemedRewards;
using Microsoft.Extensions.Logging;
using TwitchLib.Api.Interfaces;
using TwitchLib.Client.Events;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;
using TwitchLib.PubSub.Events;
using TwitchLib.PubSub.Interfaces;

namespace HeroesReplay.Core.Services.Twitch;

/*
 * https://twitchtokengenerator.com/
 */
public class TwitchBot : ITwitchBot
{
    private readonly ITwitchClient client;
    private readonly ITwitchAPI api;
    private readonly ITwitchPubSub pubSub;

    private readonly AppSettings settings;
    private readonly ConnectionCredentials credentials;
    private readonly ILogger<TwitchBot> logger;

    private readonly IOnRewardHandler onRewardHandler;
    private readonly IOnMessageHandler onMessageHandler;
    private readonly CancellationTokenProvider tokenProvider;
    private int chatBackoffSeconds = 1;
    private int pubSubBackoffSeconds = 1;
    private bool chatReconnecting;
    private bool pubSubReconnecting;
    private readonly object reconnectLock = new object();

    public TwitchBot(
        ILogger<TwitchBot> logger,
        AppSettings settings,
        ConnectionCredentials credentials,
        ITwitchAPI api,
        ITwitchPubSub pubSub,
        ITwitchClient client,
        IOnRewardHandler onRewardHandler,
        IOnMessageHandler onMessageHandler,
        CancellationTokenProvider tokenProvider
    )
    {
        this.logger = logger;
        this.settings = settings;
        this.credentials = credentials;
        this.api = api;
        this.pubSub = pubSub;
        this.client = client;
        this.onRewardHandler = onRewardHandler;
        this.onMessageHandler = onMessageHandler;
        this.tokenProvider = tokenProvider;
    }

    public async Task InitializeAsync()
    {
        if (settings.Twitch.EnableChatBot)
        {
            client.Initialize(credentials, settings.Twitch.Channel);
            client.OnLog += Client_OnLog;
            client.OnMessageReceived += Client_OnMessageReceived;
            client.OnConnected += Client_OnConnected;
            client.OnDisconnected += Client_OnDisconnected;
            client.OnConnectionError += Client_OnConnectionError;
            client.OnJoinedChannel += Client_OnJoinedChannel;
            client.OnReconnected += Client_OnReconnected;
            client.Connect();
        }

        if (settings.Twitch.EnablePubSub)
        {
            string channelId = await GetChannelId();

            pubSub.ListenToRewards(channelId);

            pubSub.OnRewardRedeemed += PubSub_OnRewardRedeemed;
            pubSub.OnLog += PubSub_OnLog;
            pubSub.OnPubSubServiceConnected += PubSub_OnPubSubServiceConnected;
            pubSub.OnPubSubServiceError += PubSub_OnPubSubServiceError;
            pubSub.OnPubSubServiceClosed += PubSub_OnPubSubServiceClosed;

            pubSub.Connect();
        }
    }

    private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
    {
        onMessageHandler.Handle(e);
    }

    private void PubSub_OnLog(object sender, TwitchLib.PubSub.Events.OnLogArgs e)
    {
        logger.LogDebug($"{e.Data}");
    }

    private void Client_OnConnectionError(object sender, OnConnectionErrorArgs e) =>
        logger.LogError(e.Error.Message, "OnConnectionError");

    private void PubSub_OnPubSubServiceConnected(object sender, EventArgs e)
    {
        pubSubBackoffSeconds = 1;
        logger.LogInformation("Twitch PubSub connected. Sending topics.");
        pubSub.SendTopics(settings.Twitch.AccessToken, unlisten: false);
    }

    private void PubSub_OnPubSubServiceClosed(object sender, EventArgs e)
    {
        logger.LogWarning("Twitch PubSub closed. Reconnecting.");
        _ = ReconnectPubSubAsync();
    }

    private void PubSub_OnPubSubServiceError(object sender, OnPubSubServiceErrorArgs e)
    {
        logger.LogError(e.Exception, "OnPubSubServiceError");
        _ = ReconnectPubSubAsync();
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

    private async Task ReconnectPubSubAsync()
    {
        lock (reconnectLock)
        {
            if (pubSubReconnecting || !settings.Twitch.EnablePubSub)
            {
                return;
            }

            pubSubReconnecting = true;
        }

        try
        {
            int delay = pubSubBackoffSeconds;
            pubSubBackoffSeconds = Math.Min(30, pubSubBackoffSeconds * 2);
            await Task.Delay(TimeSpan.FromSeconds(delay), tokenProvider.Token)
                .ConfigureAwait(false);
            pubSub.Connect();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            logger.LogWarning(e, "Twitch PubSub reconnect failed.");
        }
        finally
        {
            lock (reconnectLock)
            {
                pubSubReconnecting = false;
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

    private void PubSub_OnRewardRedeemed(object sender, OnRewardRedeemedArgs e)
    {
        onRewardHandler.Handle(e);
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

    private async Task<string> GetChannelId()
    {
        var userResponse = await api.Helix.Users.GetUsersAsync(
            logins: new List<string>() { settings.Twitch.Channel }
        );
        var channelId = userResponse.Users[0].Id;
        return channelId;
    }
}
