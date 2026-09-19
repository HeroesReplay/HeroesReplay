using System;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Observer;

public interface IObserverPanelRequests
{
    TimeSpan ShowDuration { get; }
    TimeSpan Cooldown { get; }
    ObserverPanelTryResult TryRequest(Panel panel, string username);
    bool TryConsume(out Panel panel, out string requestedBy);
    void MarkHidden(Panel panel);
}

public readonly record struct ObserverPanelTryResult(
    ObserverPanelTryStatus Status,
    TimeSpan? CooldownRemaining,
    string Username,
    Panel Panel
);

public enum ObserverPanelTryStatus
{
    Accepted,
    AlreadyVisible,
    Cooldown,
}
