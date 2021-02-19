using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;

using Polly;

using System;
using System.Threading.Tasks;

using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchLib.PubSub;

namespace HeroesReplay.Core.Services.Twitch
{
    public class TwitchBot : ITwitchBot
    {
        private readonly TwitchClient client;
        private readonly AppSettings settings;
        private readonly ConnectionCredentials credentials;
        private readonly TwitchPubSub pubSub;
        private readonly ILogger<TwitchBot> logger;
        private readonly IReplayRequestQueue replayRequests;

        public TwitchBot(ILogger<TwitchBot> logger, AppSettings settings, ConnectionCredentials credentials, TwitchPubSub pubSub, TwitchClient client, IReplayRequestQueue requestQueue)
        {
            this.logger = logger;
            this.settings = settings;
            this.credentials = credentials;
            this.pubSub = pubSub;
            this.client = client;
            this.replayRequests = requestQueue;
        }

        public void Connect()
        {
            client.Initialize(credentials, settings.Twitch.Channel);
            client.OnLog += Client_OnLog;
            client.OnJoinedChannel += Client_OnJoinedChannel;
            client.OnMessageReceived += Client_OnMessageReceived;
            client.OnWhisperReceived += Client_OnWhisperReceived;
            client.OnNewSubscriber += Client_OnNewSubscriber;
            client.OnConnected += Client_OnConnected;
            client.Connect();

            client.OnDisconnected += Client_OnDisconnected;

            pubSub.OnRewardRedeemed += PubSub_OnRewardRedeemed;
            pubSub.OnPubSubServiceError += PubSub_OnPubSubServiceError;
            pubSub.OnPubSubServiceClosed += PubSub_OnPubSubServiceClosed;
            pubSub.Connect();
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
            Task.Run(async () =>
            {
                await Policy
                    .Handle<Exception>()
                    .RetryAsync(5, (exception, retryAttempt) => { })
                    .ExecuteAsync(async () =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Message) && int.TryParse(e.Message.Trim(), out int replayId))
                        {
                            await replayRequests.EnqueueRequestAsync(new ReplayRequest { Login = e.Login, ReplayId = replayId });
                        }
                        else
                        {
                            logger.LogDebug($"{e.TimeStamp}: {e.RewardId} - {e.RewardCost}");
                        }
                    });
            });
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
