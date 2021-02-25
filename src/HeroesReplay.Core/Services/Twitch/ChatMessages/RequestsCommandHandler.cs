using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Twitch.Rewards;

using Microsoft.Extensions.Logging;

using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;

namespace HeroesReplay.Core.Services.Twitch.ChatMessages
{
    public class RequestsCommandHandler : IMessageHandler
    {
        private readonly ILogger<RequestsCommandHandler> logger;
        private readonly ITwitchClient twitchClient;
        private readonly AppSettings settings;
        private readonly IRequestQueue requestQueue;
        private readonly Regex regex = new Regex("(?<command>!requests)\\s(?<position>\\d)\\z", RegexOptions.Compiled);

        public RequestsCommandHandler(ILogger<RequestsCommandHandler> logger, ITwitchClient twitchClient, AppSettings settings, IRequestQueue requestQueue)
        {
            this.logger = logger;
            this.twitchClient = twitchClient;
            this.settings = settings;
            this.requestQueue = requestQueue;
        }

        public bool CanHandle(ChatMessage chatMessage) => !string.IsNullOrWhiteSpace(chatMessage.Message) && chatMessage.Message.StartsWith("!requests");

        public void Execute(ChatMessage chatMessage)
        {
            Task.Run(async () =>
            {
                try
                {
                    if (chatMessage.Message.Equals("!requests"))
                    {
                        var count = await requestQueue.GetItemsInQueue();
                        twitchClient.SendMessage(settings.Twitch.Channel, $"{chatMessage.Username}, requests in queue: {count}", settings.Twitch.DryRunMode);
                    }
                    else if (chatMessage.Message.Equals("!requests me"))
                    {
                        var response = await requestQueue.FindNextByLoginAsync(chatMessage.Username);

                        if (response != null)
                        {
                            var (item, position) = response.Value;
                            var replay = item.HeroesProfileReplay;
                            twitchClient.SendMessage(settings.Twitch.Channel, $"{chatMessage.Username}, your next request is {replay.Map} ({replay.Rank}) position: {position}.", settings.Twitch.DryRunMode);
                        }
                        else
                        {
                            twitchClient.SendMessage(settings.Twitch.Channel, $"{chatMessage.Username}, you have nothing in the queue. Spend some sadism bruh.", settings.Twitch.DryRunMode);
                        }
                    }
                    else if (chatMessage.Message.Equals("!requests remove"))
                    {
                        var response = await requestQueue.RemoveItemAsync(chatMessage.Username);

                        if (response != null)
                        {
                            var (item, position) = response.Value;
                            var replay = item.HeroesProfileReplay;
                            twitchClient.SendMessage(settings.Twitch.Channel, $"{chatMessage.Username}, you have removed your request {replay.Map} ({replay.Rank}) from the queue at position: {position}.", settings.Twitch.DryRunMode);
                        }
                        else
                        {
                            twitchClient.SendMessage(settings.Twitch.Channel, $"{chatMessage.Username}, you have nothing in the queue to remove.", settings.Twitch.DryRunMode);
                        }
                    }
                    else if (regex.IsMatch(chatMessage.Message))
                    {
                        var position = int.Parse(regex.Match(chatMessage.Message).Groups["position"].Value);
                        var item = await requestQueue.FindByIndexAsync(position);

                        if (item != null)
                        {
                            var replay = item.HeroesProfileReplay;
                            twitchClient.SendMessage(settings.Twitch.Channel, $"{chatMessage.Username}, request at position {position} is {replay.Map} ({replay.Rank})", settings.Twitch.DryRunMode);
                        }
                        else
                        {
                            twitchClient.SendMessage(settings.Twitch.Channel, $"{chatMessage.Username}, there is no request at position {position}", settings.Twitch.DryRunMode);
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not process user request");
                }
            });
        }
    }
}
