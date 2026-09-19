using System;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Observer;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;

namespace HeroesReplay.Core.Services.Twitch.ChatMessages;

public class StatsCommandHandler : IMessageHandler
{
    private readonly ILogger<StatsCommandHandler> logger;
    private readonly ITwitchClient twitchClient;
    private readonly AppSettings settings;
    private readonly IStatsPanelController statsPanel;

    public StatsCommandHandler(
        ILogger<StatsCommandHandler> logger,
        ITwitchClient twitchClient,
        AppSettings settings,
        IStatsPanelController statsPanel
    )
    {
        this.logger = logger;
        this.twitchClient = twitchClient;
        this.settings = settings;
        this.statsPanel = statsPanel;
    }

    public bool CanHandle(ChatMessage chatMessage) =>
        !string.IsNullOrWhiteSpace(chatMessage.Message)
        && chatMessage.Message.Trim().Equals("!stats", StringComparison.OrdinalIgnoreCase);

    public void Execute(ChatMessage chatMessage)
    {
        try
        {
            StatsPanelTryResult result = statsPanel.TryRequest(chatMessage.Username);
            string reply = result.Status switch
            {
                StatsPanelTryStatus.Accepted =>
                    $"{chatMessage.Username}, showing stats for {(int)statsPanel.ShowDuration.TotalSeconds}s.",
                StatsPanelTryStatus.AlreadyVisible =>
                    $"{chatMessage.Username}, stats are already up.",
                StatsPanelTryStatus.Cooldown =>
                    $"{chatMessage.Username}, stats on cooldown for {FormatRemaining(result.CooldownRemaining)}.",
                _ => $"{chatMessage.Username}, could not show stats.",
            };

            twitchClient.SendMessage(settings.Twitch.Channel, reply, settings.Twitch.DryRunMode);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not handle !stats from {User}", chatMessage.Username);
        }
    }

    private static string FormatRemaining(TimeSpan? remaining)
    {
        if (!remaining.HasValue)
        {
            return "a bit";
        }

        int minutes = (int)remaining.Value.TotalMinutes;
        int seconds = remaining.Value.Seconds;
        if (minutes > 0)
        {
            return $"{minutes}m {seconds}s";
        }

        return $"{seconds}s";
    }
}
