using System;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Spectating.Control;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;

namespace HeroesReplay.Core.Twitch.ChatMessages;

public class PanelCommandHandler : IMessageHandler
{
    private readonly ILogger<PanelCommandHandler> logger;
    private readonly ITwitchClient twitchClient;
    private readonly AppSettings settings;
    private readonly IObserverPanelRequests panels;

    public PanelCommandHandler(
        ILogger<PanelCommandHandler> logger,
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
        return TryMode(chatMessage?.Message, out _);
    }

    public void Execute(ChatMessage chatMessage)
    {
        try
        {
            if (!TryMode(chatMessage.Message, out bool enabled))
            {
                return;
            }

            panels.SetAutomaticTalents(enabled);
            string reply = enabled
                ? $"{chatMessage.Username}, talent panel is on."
                : $"{chatMessage.Username}, talent panel is off.";
            twitchClient.SendMessage(settings.Twitch.Channel, reply, settings.Twitch.DryRunMode);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not toggle the talent panel.");
        }
    }

    private static bool TryMode(string text, out bool enabled)
    {
        enabled = false;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !parts[0].Equals("!panel", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (parts[1].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }

        return parts[1].Equals("off", StringComparison.OrdinalIgnoreCase);
    }
}
