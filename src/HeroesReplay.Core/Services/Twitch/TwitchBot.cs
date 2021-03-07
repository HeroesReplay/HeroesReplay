using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeroesReplay.Core.Services.Twitch.ChatMessages;
using HeroesReplay.Core.Services.Twitch.RedeemedRewards;
using TwitchLib.Api.Interfaces;
using TwitchLib.Client.Events;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;
using TwitchLib.PubSub.Events;
using TwitchLib.PubSub.Interfaces;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Twitch
{
    /*
     * https://twitchtokengenerator.com/
     */
    public class TwitchBot : ITwitchBot
    {
        private readonly ITwitchClient client;
        private readonly ITwitchAPI api;
        private readonly ITwitchPubSub pubSub;

        private readonly IOptions<AppSettings> settings;
        private readonly ConnectionCredentials credentials;
        private readonly ILogger<TwitchBot> logger;

        private readonly IOnRewardHandler onRewardHandler;
        private readonly IOnMessageHandler onMessageHandler;

        public TwitchBot(
            ILogger<TwitchBot> logger,
            IOptions<AppSettings> settings,
            ConnectionCredentials credentials,
            ITwitchAPI api,
            ITwitchPubSub pubSub,
            ITwitchClient client,
            IOnRewardHandler onRewardHandler,
            IOnMessageHandler onMessageHandler)
        {
            this.logger = logger;
            this.settings = settings;
            this.credentials = credentials;
            this.api = api;
            this.pubSub = pubSub;
            this.client = client;
            this.onRewardHandler = onRewardHandler;
            this.onMessageHandler = onMessageHandler;
        }

        public async Task InitializeAsync()
        {
            if (settings.Value.Twitch.EnableChatBot)
            {
                client.Initialize(credentials, settings.Value.Twitch.Channel);
                UnWireChatEvents();
                WireChatEvents();
            }

            if (settings.Value.Twitch.EnablePubSub)
            {
                string channelId = await GetChannelId();

                pubSub.ListenToRewards(channelId);
                UnWirePubSubEvents();
                WirePubSubEvents();
                pubSub.Connect();
            }
        }

        private void WirePubSubEvents()
        {
            pubSub.OnRewardRedeemed += PubSub_OnRewardRedeemed;
            pubSub.OnLog += PubSub_OnLog;
            pubSub.OnPubSubServiceConnected += PubSub_OnPubSubServiceConnected;
            pubSub.OnPubSubServiceError += PubSub_OnPubSubServiceError;
            pubSub.OnPubSubServiceClosed += PubSub_OnPubSubServiceClosed;
        }

        private void UnWirePubSubEvents()
        {
            pubSub.OnRewardRedeemed -= PubSub_OnRewardRedeemed;
            pubSub.OnLog -= PubSub_OnLog;
            pubSub.OnPubSubServiceConnected -= PubSub_OnPubSubServiceConnected;
            pubSub.OnPubSubServiceError -= PubSub_OnPubSubServiceError;
            pubSub.OnPubSubServiceClosed -= PubSub_OnPubSubServiceClosed;
        }

        private void WireChatEvents()
        {
            client.OnLog += Client_OnLog;
            client.OnMessageReceived += Client_OnMessageReceived;
            client.OnConnected += Client_OnConnected;
            client.OnDisconnected += Client_OnDisconnected;
            client.OnConnectionError += Client_OnConnectionError;
        }

        private void UnWireChatEvents()
        {
            client.OnLog -= Client_OnLog;
            client.OnMessageReceived -= Client_OnMessageReceived;
            client.OnConnected -= Client_OnConnected;
            client.OnDisconnected -= Client_OnDisconnected;
            client.OnConnectionError -= Client_OnConnectionError;
        }

        private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            onMessageHandler.Handle(e);
        }

        private void PubSub_OnLog(object sender, TwitchLib.PubSub.Events.OnLogArgs e)
        {
            logger.LogDebug($"{e.Data}");
        }

        private void Client_OnConnectionError(object sender, OnConnectionErrorArgs e) => logger.LogError(e.Error.Message, "OnConnectionError");


        private void PubSub_OnPubSubServiceConnected(object sender, EventArgs e)
        {
            logger.LogInformation("Connected. Sending topics to subscribe to.");

            pubSub.SendTopics(settings.Value.Twitch.AccessToken, unlisten: false);
        }

        private void PubSub_OnPubSubServiceClosed(object sender, EventArgs e)
        {
            logger.LogInformation("Connected. Sending topics to subscribe to.");
        }

        private void PubSub_OnPubSubServiceError(object sender, OnPubSubServiceErrorArgs e)
        {
            logger.LogError(e.Exception, "OnPubSubServiceError");
        }

        private void Client_OnDisconnected(object sender, OnDisconnectedEventArgs e)
        {
            client.Connect();
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
            logger.LogDebug($"Connected to {e.AutoJoinChannel}");
        }

        private void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            logger.LogInformation($"{e.BotUsername} joined {e.Channel}");
        }

        private async Task<string> GetChannelId()
        {
            var userResponse = await api.Helix.Users.GetUsersAsync(logins: new List<string>() { settings.Value.Twitch.Channel });
            var channelId = userResponse.Users[0].Id;
            return channelId;
        }

        public async Task StartAsync()
        {
            await InitializeAsync();
        }

        public async Task StopAsync()
        {
            if (settings.Value.Twitch.EnableChatBot)
            {
                this.client.Disconnect();
            }

            if (settings.Value.Twitch.EnablePubSub)
            {
                this.pubSub.Disconnect();
            }
        }
    }
}