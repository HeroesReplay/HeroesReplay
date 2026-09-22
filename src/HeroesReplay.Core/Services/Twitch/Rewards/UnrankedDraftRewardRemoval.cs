using System.Collections.Generic;

namespace HeroesReplay.Core.Services.Twitch.Rewards;

public sealed class UnrankedDraftRewardRemoval
{
    public UnrankedDraftRewardRemoval(IReadOnlyList<string> deleted, IReadOnlyList<string> failed)
    {
        Deleted = deleted;
        Failed = failed;
    }

    public IReadOnlyList<string> Deleted { get; }

    public IReadOnlyList<string> Failed { get; }
}
