using System;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Twitch.Rewards;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;

namespace HeroesReplay.Core.Services.Twitch.ChatMessages;

public class QueueCommandHandler : IMessageHandler
{
    private readonly ILogger<QueueCommandHandler> logger;
    private readonly ITwitchClient twitchClient;
    private readonly AppSettings settings;
    private readonly IRequestQueue requestQueue;

    public QueueCommandHandler(
        ILogger<QueueCommandHandler> logger,
        ITwitchClient twitchClient,
        AppSettings settings,
        IRequestQueue requestQueue
    )
    {
        this.logger = logger;
        this.twitchClient = twitchClient;
        this.settings = settings;
        this.requestQueue = requestQueue;
    }

    public bool CanHandle(ChatMessage chatMessage) =>
        QueueChatCommand.TryRead(chatMessage?.Message, out _, out _);

    public async void Execute(ChatMessage chatMessage)
    {
        try
        {
            if (
                !QueueChatCommand.TryRead(
                    chatMessage.Message,
                    out QueueChatAction action,
                    out int position
                )
            )
            {
                return;
            }

            if (action == QueueChatAction.Count)
            {
                int count = await requestQueue.GetItemsInQueue();
                Send(
                    $"{chatMessage.Username}, requests in queue: {count}. !requests me, or !requests [number]."
                );
            }
            else if (action == QueueChatAction.Mine)
            {
                var response = await requestQueue.FindNextByLoginAsync(chatMessage.Username);
                if (response != null)
                {
                    var (item, index) = response.Value;
                    Send(
                        $"{chatMessage.Username}, your next request is {Describe(item)} position: {index}."
                    );
                }
                else
                {
                    Send(
                        $"{chatMessage.Username}, you have nothing in the queue. Spend some sadism bruh."
                    );
                }
            }
            else if (action == QueueChatAction.Remove)
            {
                var response = await requestQueue.RemoveItemAsync(chatMessage.Username);
                if (response != null)
                {
                    var (item, index) = response.Value;
                    Send(
                        $"{chatMessage.Username}, you have removed your request {Describe(item)} from the queue at position: {index}."
                    );
                }
                else
                {
                    Send($"{chatMessage.Username}, you have nothing in the queue to remove.");
                }
            }
            else if (action == QueueChatAction.At)
            {
                RewardQueueItem item = await requestQueue.FindByIndexAsync(position);
                if (item != null)
                {
                    Send(
                        $"{chatMessage.Username}, request at position {position} is {Describe(item)}"
                    );
                }
                else
                {
                    Send($"{chatMessage.Username}, there is no request at position {position}");
                }
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not process user request");
        }
    }

    private static string Describe(RewardQueueItem item)
    {
        string label = ReplayLabel.MapAndRank(
            item?.HeroesProfileReplay?.Map,
            item?.HeroesProfileReplay?.Rank
        );
        if (ReplayRequestKind.ViewerEnteredReplayId(item))
        {
            return label + " replay " + item.Request.ReplayId.Value;
        }

        return label;
    }

    private void Send(string message)
    {
        twitchClient.SendMessage(settings.Twitch.Channel, message, settings.Twitch.DryRunMode);
    }
}
