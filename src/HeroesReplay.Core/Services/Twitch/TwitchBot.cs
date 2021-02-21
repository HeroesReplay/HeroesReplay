using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Runner;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly IGameData gameData;

        private List<ChannelPointsMapReward> MapRewards;

        /*
         * If we have prediction data set, we should not allow users to request the replay id.
         * We also need to figure out how to randomize the next replay selection process
         * so that its not guessable for once predictions can be automated via the api.
         */
        // private OnPredictionArgs PredictionData { get; set; }

        private JoinedChannel JoinedChannel => client.GetJoinedChannel(settings.Twitch.Channel);

        public TwitchBot(
            ILogger<TwitchBot> logger,
            AppSettings settings,
            ConnectionCredentials credentials,
            ISessionHolder sessionHolder,
            TwitchAPI api,
            TwitchPubSub pubSub,
            TwitchClient client,
            IReplayRequestQueue requestQueue,
            IGameData gameData)
        {
            this.logger = logger;
            this.settings = settings;
            this.credentials = credentials;
            this.sessionHolder = sessionHolder;
            this.api = api;
            this.pubSub = pubSub;
            this.client = client;
            this.requestQueue = requestQueue;
            this.gameData = gameData;
        }

        public async Task InitializeAsync()
        {
            ConfigureRewards();

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
                client.OnConnectionError += Client_OnConnectionError;
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

        private void ConfigureRewards()
        {
            var tiers = Enum.GetValues(typeof(Tier)).OfType<Tier>().ToList();
            MapRewards = gameData.Maps
                .SelectMany(map => tiers.Select(tier => new ChannelPointsMapReward($"{map.Name} ({tier})", map, tier)))
                .Concat(gameData.Maps.Select(map => new ChannelPointsMapReward($"{map.Name}", map, tier: null)))
                .ToList();
        }

        private void Client_OnConnectionError(object sender, OnConnectionErrorArgs e)
        {
            logger.LogError(e.Error.Message, "OnConnectionError");
        }

        private void PubSub_OnPrediction(object sender, OnPredictionArgs e)
        {
            // PredictionData = e;
        }

        private void PubSub_OnChannelSubscription(object sender, OnChannelSubscriptionArgs e)
        {
            // TODO: Thank the user for the subscription
        }

        private void PubSub_OnPubSubServiceConnected(object sender, EventArgs e)
        {
            logger.LogInformation("Connected. Sending topics to subscribe to.");
            pubSub.SendTopics(settings.Twitch.AccessToken, unlisten: false);
        }

        private void PubSub_OnPubSubServiceClosed(object sender, EventArgs e)
        {

        }

        private void PubSub_OnPubSubServiceError(object sender, OnPubSubServiceErrorArgs e)
        {
            logger.LogError(e.Exception, "OnPubSubServiceError");
        }

        private void Client_OnDisconnected(object sender, TwitchLib.Communication.Events.OnDisconnectedEventArgs e)
        {
            this.client.Connect();
        }

        private void PubSub_OnRewardRedeemed(object sender, OnRewardRedeemedArgs e)
        {
            try
            {
                if (settings.Twitch.EnableReplayRequests)
                {
                    if (e.RewardTitle.Equals(settings.Twitch.RewardReplayId, StringComparison.OrdinalIgnoreCase))
                    {
                        HandleRequestReplayIdReward(e);
                    }
                    else if (e.RewardTitle.Equals(settings.Twitch.RewardARAM, StringComparison.OrdinalIgnoreCase))
                    {
                        HandleARAMReward(e);
                    }
                    else
                    {
                        if (settings.Twitch.EnableMapRewards)
                        {
                            ChannelPointsMapReward channelPointsMapReward = MapRewards.Find(mapReward => mapReward.Title.Equals(e.RewardTitle, StringComparison.OrdinalIgnoreCase));

                            if (channelPointsMapReward != null)
                            {
                                HandleMapReward(e, channelPointsMapReward);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not process reward redeemed.");
            }
        }

        private void HandleARAMReward(OnRewardRedeemedArgs e)
        {
            Task.Factory.StartNew(async () =>
            {
                ReplayRequest request = new ReplayRequest
                {
                    Login = e.Login,
                    Map = null,
                    Tier = null,
                    GameMode = GameMode.ARAM
                };

                ReplayRequestResponse response = await requestQueue.EnqueueRequestAsync(request);

                if (settings.Twitch.EnableChatBot)
                {
                    string message = $"{e.DisplayName}, {response.Message}";
                    client.SendMessage(JoinedChannel, message, dryRun: settings.Twitch.DryRunMode);
                }

            }, TaskCreationOptions.LongRunning);
        }

        private void HandleMapReward(OnRewardRedeemedArgs e, ChannelPointsMapReward reward)
        {
            Task.Factory.StartNew(async () =>
            {
                ReplayRequest request = new ReplayRequest
                {
                    Login = e.Login,
                    Map = reward.Map,
                    Tier = reward.Tier,
                    GameMode = null
                };

                ReplayRequestResponse response = await requestQueue.EnqueueRequestAsync(request);

                if (settings.Twitch.EnableChatBot)
                {
                    string message = $"{e.DisplayName}, {response.Message}";
                    client.SendMessage(JoinedChannel, message, dryRun: settings.Twitch.DryRunMode);
                }

            }, TaskCreationOptions.LongRunning);
        }

        private void HandleRequestReplayIdReward(OnRewardRedeemedArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Message) && int.TryParse(e.Message.Trim(), out int replayId))
            {
                Task.Factory.StartNew(async () =>
                {
                    ReplayRequestResponse response = await requestQueue.EnqueueRequestAsync(new ReplayRequest { Login = e.Login, ReplayId = replayId });

                    if (settings.Twitch.EnableChatBot)
                    {
                        string message = $"{e.DisplayName}, {response.Message}";
                        client.SendMessage(JoinedChannel, message, dryRun: settings.Twitch.DryRunMode);
                    }

                }, TaskCreationOptions.LongRunning);
            }
            else
            {
                logger.LogDebug($"{e.TimeStamp}: {e.RewardId} - {e.RewardCost}");
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
            var userResponse = await api.Helix.Users.GetUsersAsync(logins: new List<string>() { settings.Twitch.Account });
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
