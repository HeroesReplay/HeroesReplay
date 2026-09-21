using System.Threading;

namespace HeroesReplay.Core.Services.Connectivity;

public sealed class HeroesProfileResume : IHeroesProfileResume
{
    private int pending;

    public bool IsPending => Volatile.Read(ref pending) != 0;

    public void Arm() => Interlocked.Exchange(ref pending, 1);

    public bool Consume() => Interlocked.Exchange(ref pending, 0) != 0;
}
