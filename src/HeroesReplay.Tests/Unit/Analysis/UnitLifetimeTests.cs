using System;
using System.Linq;
using HeroesReplay.Core.Extensions;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class UnitLifetimeTests : IClassFixture<ReplayFixture>
{
    private readonly ReplayFixture fixture;

    public UnitLifetimeTests(ReplayFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void NullTimeSpanDiedIsAliveAfterBirth()
    {
        var living = fixture
            .Replay.Players.SelectMany(p => p.HeroUnits)
            .FirstOrDefault(u => u.TimeSpanDied == null);
        Assert.NotNull(living);
        Assert.True(living.IsAliveAt(living.TimeSpanBorn.Add(TimeSpan.FromSeconds(1))));
        Assert.False(living.IsAliveAt(living.TimeSpanBorn));

        var dead = fixture
            .Replay.Players.SelectMany(p => p.HeroUnits)
            .First(u =>
                u.TimeSpanDied.HasValue
                && u.TimeSpanDied.Value > u.TimeSpanBorn.Add(TimeSpan.FromSeconds(1))
            );
        Assert.True(dead.IsAliveAt(dead.TimeSpanDied.Value - TimeSpan.FromSeconds(1)));
        Assert.False(dead.IsAliveAt(dead.TimeSpanDied.Value));
    }
}
