using System;
using HeroesReplay.Core;
using HeroesReplay.Core.Services.Connectivity;
using Xunit;

namespace HeroesReplay.Tests.Unit.Connectivity;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class OutageModeTests
{
    [Fact]
    public void Decide_DistinguishesShortExtendedAndRecovery()
    {
        Assert.Equal(OperatingMode.Online, OutageMode.Decide(true, TimeSpan.Zero, TimeSpan.Zero));
        Assert.Equal(
            OperatingMode.ShortOutage,
            OutageMode.Decide(
                false,
                TimeSpan.FromHours(1).Subtract(TimeSpan.FromSeconds(1)),
                TimeSpan.Zero
            )
        );
        Assert.Equal(
            OperatingMode.ExtendedOutage,
            OutageMode.Decide(false, TimeSpan.FromHours(1), TimeSpan.Zero)
        );
        Assert.Equal(
            OperatingMode.ExtendedOutage,
            OutageMode.Decide(false, TimeSpan.FromHours(6), TimeSpan.Zero)
        );
        Assert.Equal(
            OperatingMode.Recovering,
            OutageMode.Decide(true, TimeSpan.FromHours(6), TimeSpan.FromMinutes(1))
        );
        Assert.Equal(
            OperatingMode.Online,
            OutageMode.Decide(true, TimeSpan.FromHours(6), OutageMode.RecoverStableFor)
        );
    }

    [Fact]
    public void MayDownload_PausesOnlyTheExtendedOutage()
    {
        Assert.True(OutageMode.MayDownload(OperatingMode.Online));
        Assert.True(OutageMode.MayDownload(OperatingMode.ShortOutage));
        Assert.True(OutageMode.MayDownload(OperatingMode.Recovering));
        Assert.False(OutageMode.MayDownload(OperatingMode.ExtendedOutage));
    }

    [Fact]
    public void MaySpectate_PausesOnlyTheExtendedOutage()
    {
        Assert.True(OutageMode.MaySpectate(OperatingMode.Online));
        Assert.True(OutageMode.MaySpectate(OperatingMode.ShortOutage));
        Assert.True(OutageMode.MaySpectate(OperatingMode.Recovering));
        Assert.False(OutageMode.MaySpectate(OperatingMode.ExtendedOutage));
        Assert.False(
            OutageMode.MaySpectate(
                OutageMode.Decide(false, OutageMode.ExtendedAfter, TimeSpan.Zero)
            )
        );
        Assert.True(
            OutageMode.MaySpectate(
                OutageMode.Decide(
                    false,
                    OutageMode.ExtendedAfter.Subtract(TimeSpan.FromSeconds(1)),
                    TimeSpan.Zero
                )
            )
        );
    }

    [Fact]
    public void MayStartReplay_PausesAnExtendedOutageBeforeTheNextLaunch()
    {
        Assert.False(Engine.MayStartReplay(false, TimeSpan.FromHours(2), TimeSpan.Zero));
        Assert.True(Engine.MayStartReplay(true, TimeSpan.Zero, TimeSpan.Zero));
        Assert.True(Engine.MayStartReplay(false, TimeSpan.FromMinutes(5), TimeSpan.Zero));
    }
}
