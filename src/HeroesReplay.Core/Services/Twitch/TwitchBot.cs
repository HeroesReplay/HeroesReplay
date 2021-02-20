using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.PubSub;
using TwitchLib.PubSub.Events;

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
        private readonly ISessionHolder sessionHolder;
        private readonly ILogger<TwitchBot> logger;
        private readonly IReplayRequestQueue requestQueue;

        private OnPredictionArgs PredictionData { get; set; }

        private JoinedChannel JoinedChannel => client.GetJoinedChannel(settings.Twitch.Channel);

        public TwitchBot(ILogger<TwitchBot> logger, AppSettings settings, ConnectionCredentials credentials, ISessionHolder sessionHolder, TwitchAPI api, TwitchPubSub pubSub, TwitchClient client, IReplayRequestQueue requestQueue)
        {
            this.logger = logger;
            this.settings = settings;
            this.credentials = credentials;
            this.sessionHolder = sessionHolder;
            this.api = api;
            this.pubSub = pubSub;
            this.client = client;
            this.requestQueue = requestQueue;
        }

        public async Task ConnectAsync()
        {
            if (settings.Twitch.EnableChatBot)
            {
                client.Initialize(credentials, settings.Twitch.Channel);
                client.OnLog += Client_OnLog;
                client.OnMessageReceived += Client_OnMessageReceived;
                client.OnWhisperReceived += Client_OnWhisperReceived;
                client.OnJoinedChannel += Client_OnJoinedChannel;
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
                pubSub.OnChannelSubscription += PubSub_OnChannelSubscription;
                pubSub.OnPubSubServiceConnected += PubSub_OnPubSubServiceConnected;
                pubSub.OnPubSubServiceError += PubSub_OnPubSubServiceError;
                pubSub.OnPubSubServiceClosed += PubSub_OnPubSubServiceClosed;
                pubSub.OnPrediction += PubSub_OnPrediction;
                pubSub.Connect();
            }
        }

        private void PubSub_OnPrediction(object sender, OnPredictionArgs e)
        {
            PredictionData = e;
        }

        private void PubSub_OnChannelSubscription(object sender, OnChannelSubscriptionArgs e)
        {
            // TODO: Thank the user for the subscription
        }

        private void PubSub_OnPubSubServiceConnected(object sender, EventArgs e)
        {
            logger.LogInformation("Connected. Sending topics to subscribe to.");
            pubSub.SendTopics(this.settings.Twitch.AccessToken, unlisten: false);
        }

        private void PubSub_OnPubSubServiceClosed(object sender, EventArgs e)
        {

        }

        private void PubSub_OnPubSubServiceError(object sender, OnPubSubServiceErrorArgs e)
        {

        }

        private void Client_OnDisconnected(object sender, TwitchLib.Communication.Events.OnDisconnectedEventArgs e)
        {

        }

        private void PubSub_OnRewardRedeemed(object sender, OnRewardRedeemedArgs e)
        {
            try
            {
                if (e.RewardTitle.Equals(settings.Twitch.RewardTitle, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(e.Message) && int.TryParse(e.Message.Trim(), out int replayId))
                    {
                        Task.Run(async () =>
                        {
                            // If successful
                            if (await requestQueue.EnqueueRequestAsync(new ReplayRequest { Login = e.Login, ReplayId = replayId }))
                            {
                                if (settings.Twitch.EnableChatBot)
                                {
                                    string message = $"{e.DisplayName}, your replay request ({replayId}) was added to the queue.";
                                    client.SendMessage(JoinedChannel, message, dryRun: settings.Twitch.DryRunMode);
                                }
                            }
                            else
                            {
                                if (settings.Twitch.EnableChatBot)
                                {
                                    string message = $"{e.DisplayName}, your replay request ({replayId}) was invalid.";
                                    client.SendMessage(JoinedChannel, message, dryRun: settings.Twitch.DryRunMode);
                                }
                            }

                        }).Wait();
                    }
                    else
                    {
                        logger.LogDebug($"{e.TimeStamp}: {e.RewardId} - {e.RewardCost}");
                    }
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

        private void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            logger.LogDebug($"Connected to {e.AutoJoinChannel}");
        }

        private void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {

        }

        private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            if (e.ChatMessage.Message.Equals("!current"))
            {
                if (sessionHolder.SessionData != null && sessionHolder.SessionData.ReplayId.HasValue)
                {
                    client.SendMessage(JoinedChannel, $"https://www.heroesprofile.com/Match/Single/?replayID={sessionHolder.SessionData.ReplayId.Value}");
                }
            }
        }

        private void Client_OnWhisperReceived(object sender, OnWhisperReceivedArgs e)
        {


        }

        private void Client_OnNewSubscriber(object sender, OnNewSubscriberArgs e)
        {

        }

        private async Task<string> GetChannelId()
        {
            var userResponse = await api.Helix.Users.GetUsersAsync(logins: new List<string>() { this.settings.Twitch.Account });
            var channelId = userResponse.Users[0].Id;
            return channelId;
        }

        public void Dispose()
        {
            this?.client.Disconnect();
            this?.pubSub.Disconnect();
        }
    }
}
