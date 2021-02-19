using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;

using Polly;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchLib.PubSub;

namespace HeroesReplay.Core.Services.Twitch
{
    /*
     * https://twitchtokengenerator.com/
     */
    public class TwitchBot : ITwitchBot
    {
        private readonly TwitchClient client;
        private readonly TwitchAPI api;
        private readonly TwitchPubSub pubSub;

        private readonly AppSettings settings;
        private readonly ConnectionCredentials credentials;

        private readonly ILogger<TwitchBot> logger;
        private readonly IReplayRequestQueue replayRequests;

        public TwitchBot(ILogger<TwitchBot> logger, AppSettings settings, ConnectionCredentials credentials, TwitchAPI api, TwitchPubSub pubSub, TwitchClient client, IReplayRequestQueue requestQueue)
        {
            this.logger = logger;
            this.settings = settings;
            this.credentials = credentials;
            this.api = api;
            this.pubSub = pubSub;
            this.client = client;
            this.replayRequests = requestQueue;
        }

        public async Task ConnectAsync()
        {
            if (settings.Twitch.EnableChatBot)
            {
                client.Initialize(credentials, settings.Twitch.Channel);
                client.OnLog += Client_OnLog;
                client.OnJoinedChannel += Client_OnJoinedChannel;
                client.OnMessageReceived += Client_OnMessageReceived;
                client.OnWhisperReceived += Client_OnWhisperReceived;
                client.OnNewSubscriber += Client_OnNewSubscriber;
                client.OnConnected += Client_OnConnected;
                client.OnDisconnected += Client_OnDisconnected;
                client.Connect();
            }

            if (settings.Twitch.EnablePubSub)
            {
                string channelId = await GetChannelId();

                pubSub.ListenToRewards(channelId);
                pubSub.ListenToSubscriptions(channelId);
                pubSub.ListenToFollows(channelId);

                pubSub.OnRewardRedeemed += PubSub_OnRewardRedeemed;
                pubSub.OnPubSubServiceConnected += PubSub_OnPubSubServiceConnected;
                pubSub.OnPubSubServiceError += PubSub_OnPubSubServiceError;
                pubSub.OnPubSubServiceClosed += PubSub_OnPubSubServiceClosed;
                pubSub.Connect();
            }
        }

        private async Task<string> GetChannelId()
        {
            var userResponse = await api.Helix.Users.GetUsersAsync(logins: new List<string>() { this.settings.Twitch.Account });
            var channelId = userResponse.Users[0].Id;
            return channelId;
        }

        private void PubSub_OnPubSubServiceConnected(object sender, EventArgs e)
        {
            logger.LogInformation("Connected. Sending topics to subscribe to.");

            pubSub.SendTopics(this.settings.Twitch.AccessToken, unlisten: false);
        }

        private void PubSub_OnPubSubServiceClosed(object sender, EventArgs e)
        {

        }

        private void PubSub_OnPubSubServiceError(object sender, TwitchLib.PubSub.Events.OnPubSubServiceErrorArgs e)
        {

        }

        private void Client_OnDisconnected(object sender, TwitchLib.Communication.Events.OnDisconnectedEventArgs e)
        {

        }

        private void PubSub_OnRewardRedeemed(object sender, TwitchLib.PubSub.Events.OnRewardRedeemedArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(e.Message) && int.TryParse(e.Message.Trim(), out int replayId))
                {
                    Task.Run(() => replayRequests.EnqueueRequestAsync(new ReplayRequest { Login = e.Login, ReplayId = replayId })).Wait();

                    if (settings.Twitch.EnableChatBot)
                    {
                        client.SendMessage(client.GetJoinedChannel(settings.Twitch.Channel), $"{e.DisplayName} your replay request ({replayId}) has been queued. Note that you cannot revert your reward request and the spectator only supports version: {settings.Spectate.VersionSupported}.");
                    }
                }
                else
                {
                    logger.LogDebug($"{e.TimeStamp}: {e.RewardId} - {e.RewardCost}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not process reward redeemed.");
            }
        }

        private void Client_OnLog(object sender, TwitchLib.Client.Events.OnLogArgs e)
        {
            logger.LogDebug($"{e.DateTime}: {e.BotUsername} - {e.Data}");
        }

        private void Client_OnConnected(object sender, TwitchLib.Client.Events.OnConnectedArgs e)
        {
            logger.LogDebug($"Connected to {e.AutoJoinChannel}");
        }

        private void Client_OnJoinedChannel(object sender, TwitchLib.Client.Events.OnJoinedChannelArgs e)
        {
            // client.SendMessage(e.Channel, "Hey guys! I am a bot connected via TwitchLib!", dryRun: true);
        }

        private void Client_OnMessageReceived(object sender, TwitchLib.Client.Events.OnMessageReceivedArgs e)
        {
            //if (e.ChatMessage.Message.Contains("badword"))
            //    client.TimeoutUser(e.ChatMessage.Channel, e.ChatMessage.Username, TimeSpan.FromMinutes(30), "Bad word! 30 minute timeout!");
        }

        private void Client_OnWhisperReceived(object sender, TwitchLib.Client.Events.OnWhisperReceivedArgs e)
        {
            //if (e.WhisperMessage.Username == "my_friend")
            //    client.SendWhisper(e.WhisperMessage.Username, "Hey! Whispers are so cool!!");
        }

        private void Client_OnNewSubscriber(object sender, TwitchLib.Client.Events.OnNewSubscriberArgs e)
        {

        }

        public void Dispose()
        {
            this?.client.Disconnect();
            this?.pubSub.Disconnect();
        }
    }
}
