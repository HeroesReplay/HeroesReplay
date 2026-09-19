using System;

namespace HeroesReplay.Core.Services.Observer;

public interface IStatsPanelController
{
    TimeSpan ShowDuration { get; }
    TimeSpan Cooldown { get; }
    StatsPanelTryResult TryRequest(string username);
    bool TryConsume(out string requestedBy);
    void MarkHidden();
}

public readonly record struct StatsPanelTryResult(
    StatsPanelTryStatus Status,
    TimeSpan? CooldownRemaining,
    string Username
);

public enum StatsPanelTryStatus
{
    Accepted,
    AlreadyVisible,
    Cooldown,
}
