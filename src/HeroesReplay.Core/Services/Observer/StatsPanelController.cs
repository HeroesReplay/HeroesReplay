using System;
using HeroesReplay.Core.Configuration;

namespace HeroesReplay.Core.Services.Observer;

public sealed class StatsPanelController : IStatsPanelController
{
    private readonly object gate = new();
    private readonly AppSettings settings;
    private DateTimeOffset? lastShownUtc;
    private string pendingUser;
    private bool showing;

    public StatsPanelController(AppSettings settings)
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

    public StatsPanelTryResult TryRequest(string username)
    {
        lock (gate)
        {
            if (showing)
            {
                return new StatsPanelTryResult(StatsPanelTryStatus.AlreadyVisible, null, username);
            }

            TimeSpan? remaining = CooldownRemaining;
            if (remaining.HasValue)
            {
                return new StatsPanelTryResult(StatsPanelTryStatus.Cooldown, remaining, username);
            }

            pendingUser = username;
            return new StatsPanelTryResult(StatsPanelTryStatus.Accepted, null, username);
        }
    }

    public bool TryConsume(out string requestedBy)
    {
        lock (gate)
        {
            if (showing || string.IsNullOrEmpty(pendingUser))
            {
                requestedBy = null;
                return false;
            }

            requestedBy = pendingUser;
            pendingUser = null;
            showing = true;
            lastShownUtc = DateTimeOffset.UtcNow;
            return true;
        }
    }

    public void MarkHidden()
    {
        lock (gate)
        {
            showing = false;
        }
    }

    private TimeSpan? CooldownRemaining
    {
        get
        {
            if (!lastShownUtc.HasValue)
            {
                return null;
            }

            TimeSpan elapsed = DateTimeOffset.UtcNow - lastShownUtc.Value;
            if (elapsed >= Cooldown)
            {
                return null;
            }

            return Cooldown - elapsed;
        }
    }
}
