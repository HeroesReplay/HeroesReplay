using System;
using System.Collections.Generic;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchClockPatternTests
{
    [Fact]
    public void Find_ResolvesRipRelativeGlobals()
    {
        byte[] window = PatternAt(tickDisplacement: 0x2000, speedDisplacement: 0x3000);
        List<MatchClockPattern.Site> sites = MatchClockPattern.Find(window, byteRva: 0x1000);
        Assert.Single(sites);
        Assert.Equal(0x1000 + MatchClockPattern.MovdEnd + 0x2000, sites[0].TickRva);
        Assert.Equal(0x1000 + MatchClockPattern.MulssEnd + 0x3000, sites[0].SpeedRva);
    }

    [Fact]
    public void TryAgree_RejectsTwoSitesThatPointAtDifferentGlobals()
    {
        var sites = new List<MatchClockPattern.Site> { new(0x10, 0x20), new(0x10, 0x99) };
        Assert.False(MatchClockPattern.TryAgree(sites, out _, out _));
    }

    private static byte[] PatternAt(int tickDisplacement, int speedDisplacement)
    {
        byte[] bytes = new byte[MatchClockPattern.MulssEnd];
        bytes[0] = 0x84;
        bytes[1] = 0xC0;
        bytes[2] = 0x74;
        bytes[3] = 0x0A;
        bytes[4] = 0x66;
        bytes[5] = 0x0F;
        bytes[6] = 0x6E;
        bytes[7] = 0x05;
        BitConverter.GetBytes(tickDisplacement).CopyTo(bytes, MatchClockPattern.TickDisplacement);
        bytes[12] = 0x0F;
        bytes[13] = 0x5B;
        bytes[14] = 0xC0;
        bytes[15] = 0xF3;
        bytes[16] = 0x0F;
        bytes[17] = 0x59;
        bytes[18] = 0x05;
        BitConverter.GetBytes(speedDisplacement).CopyTo(bytes, MatchClockPattern.SpeedDisplacement);
        return bytes;
    }
}
