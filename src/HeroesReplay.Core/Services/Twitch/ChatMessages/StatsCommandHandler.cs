using System;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
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
    private readonly IObserverPanelRequests panels;

    public StatsCommandHandler(
        ILogger<StatsCommandHandler> logger,
        ITwitchClient twitchClient,
        AppSettings settings,
        IObserverPanelRequests panels
    )
    {
        this.logger = logger;
        this.twitchClient = twitchClient;
        this.settings = settings;
        this.panels = panels;
    }

    public bool CanHandle(ChatMessage chatMessage)
    {
        if (string.IsNullOrWhiteSpace(chatMessage.Message))
        {
            return false;
        }

        string text = chatMessage.Message.Trim();
        return text.Equals("!stats", StringComparison.OrdinalIgnoreCase)
            || text.Equals("!talents", StringComparison.OrdinalIgnoreCase);
    }

    public void Execute(ChatMessage chatMessage)
    {
        try
        {
            bool talents = chatMessage
                .Message.Trim()
                .Equals("!talents", StringComparison.OrdinalIgnoreCase);
            Panel panel = talents ? Panel.Talents : Panel.DeathDamageRole;
            string name = talents ? "talents" : "stats";
            ObserverPanelTryResult result = panels.TryRequest(panel, chatMessage.Username);
            string reply = result.Status switch
            {
                ObserverPanelTryStatus.Accepted =>
                    $"{chatMessage.Username}, showing {name} for {(int)panels.ShowDuration.TotalSeconds}s (Ctrl+{(talents ? "1" : "2")}).",
                ObserverPanelTryStatus.AlreadyVisible =>
                    $"{chatMessage.Username}, {name} are already up.",
                ObserverPanelTryStatus.Cooldown =>
                    $"{chatMessage.Username}, {name} on cooldown for {FormatRemaining(result.CooldownRemaining)}.",
                _ => $"{chatMessage.Username}, could not show {name}.",
            };

            twitchClient.SendMessage(settings.Twitch.Channel, reply, settings.Twitch.DryRunMode);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not handle panel command from {User}", chatMessage.Username);
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
