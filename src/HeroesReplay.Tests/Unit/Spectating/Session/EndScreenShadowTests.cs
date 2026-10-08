using System;
using HeroesReplay.Core.Spectating.Session;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Session;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class EndScreenShadowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 19, 3, 0, TimeSpan.Zero);

    [Fact]
    public void Due_FromTheCoreDeathTimeUntilTheSessionEnds()
    {
        Assert.True(EndScreenShadow.Due(pastCore: true, sessionEnding: false, Now, default));
        Assert.True(EndScreenShadow.Due(true, false, Now, Now));
    }

    [Fact]
    public void Due_NotBeforeTheCoreNorAfterTheSessionEndsNorBeforeTheNextCheck()
    {
        Assert.False(EndScreenShadow.Due(pastCore: false, sessionEnding: false, Now, default));
        Assert.False(EndScreenShadow.Due(pastCore: true, sessionEnding: true, Now, default));
        Assert.False(EndScreenShadow.Due(true, false, Now, Now.AddSeconds(1)));
    }
}
