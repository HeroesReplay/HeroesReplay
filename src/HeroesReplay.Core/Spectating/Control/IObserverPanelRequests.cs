using System;
using HeroesReplay.Core.Analysis;

namespace HeroesReplay.Core.Spectating.Control;

public interface IObserverPanelRequests
{
    TimeSpan ShowDuration { get; }
    TimeSpan Cooldown { get; }
    bool AutomaticTalentsEnabled { get; }
    void SetAutomaticTalents(bool enabled);
    ObserverPanelTryResult TryRequest(Panel panel, string username);
    bool TryConsume(out Panel panel, out string requestedBy);
    void MarkHidden(Panel panel);

    /// <summary>No panel a viewer asked for is up: a new match starts with none.</summary>
    void ClearShowing();
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
