using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Twitch.Rewards;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;

namespace HeroesReplay.Core.Services.Twitch.ChatMessages
{
    public class RequestsCommandHandler : IMessageHandler
    {
        private readonly ITwitchClient twitchClient;
        private readonly AppSettings settings;
        private readonly IRequestQueue requestQueue;
        private readonly Regex regex = new Regex("(?<command>!requests)\\s(?<position>\\d)\\z");

        public RequestsCommandHandler(ITwitchClient twitchClient, AppSettings settings, IRequestQueue requestQueue)
        {
            this.twitchClient = twitchClient;
            this.settings = settings;
            this.requestQueue = requestQueue;
        }

        public bool CanHandle(ChatMessage chatMessage) => chatMessage.Message.StartsWith("!requests");

        public void Execute(ChatMessage chatMessage)
        {
            Task.Run(async () =>
            {
                try
                {
                    if (chatMessage.Message.Equals("!requests"))
                    {
                        var queueLength = await requestQueue.GetItemsInQueue();
                        twitchClient.SendMessage(settings.Twitch.Channel, $"{chatMessage.Username}, requests in queue: {queueLength}", settings.Twitch.DryRunMode);
                    }
                    else if (chatMessage.Message.Equals("!requests me"))
                    {
                        (RewardQueueItem, int Position)? response = await requestQueue.FindNextByLoginAsync(chatMessage.Username);

                        if (response != null)
                        {
                            twitchClient.SendMessage(settings.Twitch.Channel, $"{chatMessage.Username}, your next request is at position {response.Value.Position}", settings.Twitch.DryRunMode);
                        }
                        else
                        {
                            twitchClient.SendMessage(settings.Twitch.Channel, $"{chatMessage.Username}, you have nothing in the queue.", settings.Twitch.DryRunMode);
                        }
                    }
                    else if (regex.IsMatch(chatMessage.Message))
                    {
                        var position = regex.Match(chatMessage.Message).Groups["position"].Value;
                        var queueItem = await requestQueue.FindByIndexAsync(int.Parse(position));

                        if (queueItem == null)
                        {
                            twitchClient.SendMessage(settings.Twitch.Channel, $"{chatMessage.Username}, there is no request at position {position}", settings.Twitch.DryRunMode);
                        }
                        else
                        {
                            twitchClient.SendMessage(settings.Twitch.Channel, $"{chatMessage.Username}, next up: {queueItem.HeroesProfileReplay.Map} ({queueItem.HeroesProfileReplay.Rank})", settings.Twitch.DryRunMode);
                        }
                    }
                }
                catch (Exception e)
                {
                    twitchClient.SendMessage(settings.Twitch.Channel, $"{chatMessage.Username}, sorry I could not process your request.", settings.Twitch.DryRunMode);
                }
            });
        }
    }
}
