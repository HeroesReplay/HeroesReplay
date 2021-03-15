using System;
using System.Text.RegularExpressions;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Queue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;

namespace HeroesReplay.Service.Twitch.Core.ChatMessages
{
    public class QueueCommandHandler : IMessageHandler
    {
        private readonly ILogger<QueueCommandHandler> logger;
        private readonly ITwitchClient twitchClient;
        private readonly IRequestQueue requestQueue;
        private readonly TwitchOptions twitchOptions;

        private readonly Regex regex = new("(?<command>!queue)\\s(?<position>\\d)\\z", RegexOptions.Compiled);

        public QueueCommandHandler(ILogger<QueueCommandHandler> logger, ITwitchClient twitchClient, IRequestQueue requestQueue, IOptions<TwitchOptions> twitchOptions)
        {
            this.logger = logger;
            this.twitchClient = twitchClient;
            this.requestQueue = requestQueue;
            this.twitchOptions = twitchOptions.Value;
        }

        public bool CanHandle(ChatMessage chatMessage) => !string.IsNullOrWhiteSpace(chatMessage.Message) && chatMessage.Message.StartsWith("!queue");

        public async void Execute(ChatMessage chatMessage)
        {
            try
            {
                if (chatMessage.Message.Equals("!queue"))
                {
                    var count = await requestQueue.GetItemsInQueue();
                    twitchClient.SendMessage(twitchOptions.Channel, $"{chatMessage.Username}, requests in queue: {count}", twitchOptions.DryRunMode);
                }
                else if (chatMessage.Message.Equals("!queue me"))
                {
                    var response = await requestQueue.FindNextByLoginAsync(chatMessage.Username);

                    if (response != null)
                    {
                        var (item, position) = response.Value;
                        var replay = item.HeroesProfileReplay;
                        twitchClient.SendMessage(twitchOptions.Channel, $"{chatMessage.Username}, your next request is {replay.Map} ({replay.Rank}) position: {position}.", twitchOptions.DryRunMode);
                    }
                    else
                    {
                        twitchClient.SendMessage(twitchOptions.Channel, $"{chatMessage.Username}, you have nothing in the queue. Spend some sadism bruh.", twitchOptions.DryRunMode);
                    }
                }
                else if (chatMessage.Message.Equals("!queue remove"))
                {
                    var response = await requestQueue.RemoveItemAsync(chatMessage.Username);

                    if (response != null)
                    {
                        var (item, position) = response.Value;
                        var replay = item.HeroesProfileReplay;
                        twitchClient.SendMessage(twitchOptions.Channel, $"{chatMessage.Username}, you have removed your request {replay.Map} ({replay.Rank}) from the queue at position: {position}.", twitchOptions.DryRunMode);
                    }
                    else
                    {
                        twitchClient.SendMessage(twitchOptions.Channel, $"{chatMessage.Username}, you have nothing in the queue to remove.", twitchOptions.DryRunMode);
                    }
                }
                else if (regex.IsMatch(chatMessage.Message))
                {
                    var position = int.Parse(regex.Match(chatMessage.Message).Groups["position"].Value);
                    var item = await requestQueue.FindByIndexAsync(position);

                    if (item != null)
                    {
                        var replay = item.HeroesProfileReplay;
                        twitchClient.SendMessage(twitchOptions.Channel, $"{chatMessage.Username}, request at position {position} is {replay.Map} ({replay.Rank})", twitchOptions.DryRunMode);
                    }
                    else
                    {
                        twitchClient.SendMessage(twitchOptions.Channel, $"{chatMessage.Username}, there is no request at position {position}", twitchOptions.DryRunMode);
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not process user request");
            }
        }
    }
}
