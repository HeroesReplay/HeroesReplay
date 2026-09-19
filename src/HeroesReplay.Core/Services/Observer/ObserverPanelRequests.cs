using System;
using System.Collections.Generic;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Observer;

public sealed class ObserverPanelRequests : IObserverPanelRequests
{
    private readonly object gate = new();
    private readonly AppSettings settings;
    private readonly Dictionary<Panel, DateTimeOffset> lastShownUtc = new();
    private Panel? pendingPanel;
    private string pendingUser;
    private Panel? showing;

    public ObserverPanelRequests(AppSettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public TimeSpan ShowDuration =>
        settings.Spectate.StatsPanelShowDuration <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(10)
            : settings.Spectate.StatsPanelShowDuration;

    public TimeSpan Cooldown =>
        settings.Spectate.StatsPanelCooldown <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(2)
            : settings.Spectate.StatsPanelCooldown;

    public ObserverPanelTryResult TryRequest(Panel panel, string username)
    {
        lock (gate)
        {
            if (showing == panel)
            {
                return new ObserverPanelTryResult(
                    ObserverPanelTryStatus.AlreadyVisible,
                    null,
                    username,
                    panel
                );
            }

            TimeSpan? remaining = Remaining(panel);
            if (remaining.HasValue)
            {
                return new ObserverPanelTryResult(
                    ObserverPanelTryStatus.Cooldown,
                    remaining,
                    username,
                    panel
                );
            }

            pendingPanel = panel;
            pendingUser = username;
            return new ObserverPanelTryResult(
                ObserverPanelTryStatus.Accepted,
                null,
                username,
                panel
            );
        }
    }

    public bool TryConsume(out Panel panel, out string requestedBy)
    {
        lock (gate)
        {
            if (!pendingPanel.HasValue)
            {
                panel = Panel.None;
                requestedBy = null;
                return false;
            }

            panel = pendingPanel.Value;
            requestedBy = pendingUser;
            pendingPanel = null;
            pendingUser = null;
            showing = panel;
            lastShownUtc[panel] = DateTimeOffset.UtcNow;
            return true;
        }
    }

    public void MarkHidden(Panel panel)
    {
        lock (gate)
        {
            if (showing == panel)
            {
                showing = null;
            }
        }
    }

    private TimeSpan? Remaining(Panel panel)
    {
        if (!lastShownUtc.TryGetValue(panel, out DateTimeOffset shown))
        {
            return null;
        }

        TimeSpan elapsed = DateTimeOffset.UtcNow - shown;
        if (elapsed >= Cooldown)
        {
            return null;
        }

        return Cooldown - elapsed;
    }
}
