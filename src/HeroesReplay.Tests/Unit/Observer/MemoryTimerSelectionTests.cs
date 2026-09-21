using System;
using System.Collections.Generic;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MemoryTimerSelectionTests
{
    [Fact]
    public void TracksHud_WhenValueStepsWithHud_IsTrue()
    {
        Assert.True(MemoryTimerSelection.TracksHud(600, 601, 600, 601));
    }

    [Fact]
    public void TracksHud_WhenCellStaysPutWhileHudAdvances_IsFalse()
    {
        Assert.False(MemoryTimerSelection.TracksHud(600, 600, 600, 601));
    }

    [Fact]
    public void TracksHud_WhenValueIsFarFromHud_IsFalse()
    {
        Assert.False(MemoryTimerSelection.TracksHud(600, 610, 600, 601));
    }

    [Fact]
    public void TracksHud_WhenValueRewinds_IsFalse()
    {
        Assert.False(MemoryTimerSelection.TracksHud(600, 590, 600, 599));
    }

    [Fact]
    public void Ticking_KeepsOnlyCellsThatHaveStepped()
    {
        var candidates = new List<MemoryTimerCandidate>
        {
            new(0x10, MemoryTimerKind.FloatSeconds, 12, 0),
            new(0x20, MemoryTimerKind.FloatSeconds, 13, 1),
            new(0x30, MemoryTimerKind.Int32Seconds, 13, 2),
        };

        List<MemoryTimerCandidate> ticking = MemoryTimerSelection.Ticking(candidates);

        Assert.Equal(2, ticking.Count);
        Assert.Equal(0x20L, ticking[0].Address);
        Assert.Equal(0x30L, ticking[1].Address);
    }

    [Fact]
    public void KeepTicking_DropsStaticAndMissing_KeepsStepper()
    {
        var previous = new List<MemoryTimerCandidate>
        {
            new(0x1000, MemoryTimerKind.Int32Seconds, 600, 1),
            new(0x2000, MemoryTimerKind.Int32Seconds, 600, 2),
            new(0x3000, MemoryTimerKind.FloatSeconds, 600, 0),
        };
        var readings = new Dictionary<long, int> { [0x1000] = 600, [0x2000] = 601 };

        List<MemoryTimerCandidate> kept = MemoryTimerSelection.KeepTicking(
            previous,
            readings,
            600,
            601
        );

        Assert.Single(kept);
        Assert.Equal(0x2000, kept[0].Address);
        Assert.Equal(601, kept[0].Seconds);
        Assert.Equal(3, kept[0].AgreeTicks);
    }

    [Fact]
    public void KeepTicking_WhenHudAndCellStayPut_DoesNotAccrueALockTick()
    {
        var previous = new List<MemoryTimerCandidate>
        {
            new(0x2000, MemoryTimerKind.Int32Seconds, 600, 0),
        };
        var readings = new Dictionary<long, int> { [0x2000] = 600 };

        List<MemoryTimerCandidate> kept = MemoryTimerSelection.KeepTicking(
            previous,
            readings,
            600,
            600
        );

        Assert.Single(kept);
        Assert.Equal(0, kept[0].AgreeTicks);
    }

    [Fact]
    public void TryLock_RefusesOverflowUnfinishedScanAndShortStreak()
    {
        var stable = new List<MemoryTimerCandidate>
        {
            new(0x2000, MemoryTimerKind.Int32Seconds, 601, MemoryTimerSelection.TicksBeforeLock),
        };

        Assert.Null(MemoryTimerSelection.TryLock(stable, scanFinished: true, overflowed: true));
        Assert.Null(MemoryTimerSelection.TryLock(stable, scanFinished: false, overflowed: false));
        Assert.Null(
            MemoryTimerSelection.TryLock(
                new List<MemoryTimerCandidate>
                {
                    new(0x2000, MemoryTimerKind.Int32Seconds, 601, 1),
                },
                scanFinished: true,
                overflowed: false
            )
        );
    }

    [Fact]
    public void TryLock_WhenCopiesAgree_PrefersLowestIntAddress()
    {
        var copies = new List<MemoryTimerCandidate>
        {
            new(0x5000, MemoryTimerKind.FloatSeconds, 601, 3),
            new(0x3000, MemoryTimerKind.Int32Seconds, 601, 4),
            new(0x1000, MemoryTimerKind.Int32Seconds, 600, 3),
        };

        MemoryTimerCandidate? locked = MemoryTimerSelection.TryLock(
            copies,
            scanFinished: true,
            overflowed: false
        );

        Assert.True(locked.HasValue);
        Assert.Equal(0x1000, locked.Value.Address);
        Assert.Equal(MemoryTimerKind.Int32Seconds, locked.Value.Kind);
    }

    [Fact]
    public void TryLock_WhenValuesDiverge_Refuses()
    {
        var diverged = new List<MemoryTimerCandidate>
        {
            new(0x1000, MemoryTimerKind.Int32Seconds, 600, 3),
            new(0x2000, MemoryTimerKind.Int32Seconds, 603, 3),
        };

        Assert.Null(MemoryTimerSelection.TryLock(diverged, scanFinished: true, overflowed: false));
    }

    [Fact]
    public void TryLock_WhenOneCopyIsNew_Waits()
    {
        var mixed = new List<MemoryTimerCandidate>
        {
            new(0x1000, MemoryTimerKind.Int32Seconds, 601, 3),
            new(0x2000, MemoryTimerKind.Int32Seconds, 601, 0),
        };

        Assert.Null(MemoryTimerSelection.TryLock(mixed, scanFinished: true, overflowed: false));
    }

    [Fact]
    public void TryMatchStoredClock_MatchesNearbyIntAndFloat_RejectsDistantAndZero()
    {
        byte[] intBytes = BitConverter.GetBytes(600);
        Assert.True(
            MemoryTimerSelection.TryMatchStoredClock(
                intBytes,
                MemoryTimerKind.Int32Seconds,
                600,
                out int intSeconds
            )
        );
        Assert.Equal(600, intSeconds);
        Assert.False(
            MemoryTimerSelection.TryMatchStoredClock(
                intBytes,
                MemoryTimerKind.Int32Seconds,
                601,
                out _
            )
        );
        Assert.False(
            MemoryTimerSelection.TryMatchStoredClock(
                BitConverter.GetBytes(0),
                MemoryTimerKind.Int32Seconds,
                0,
                out _
            )
        );

        byte[] floatBytes = BitConverter.GetBytes(600.4f);
        Assert.True(
            MemoryTimerSelection.TryMatchStoredClock(
                floatBytes,
                MemoryTimerKind.FloatSeconds,
                600,
                out int floatSeconds
            )
        );
        Assert.Equal(600, floatSeconds);
        Assert.False(
            MemoryTimerSelection.TryMatchStoredClock(
                BitConverter.GetBytes(600.0f),
                MemoryTimerKind.FloatSeconds,
                600,
                out _
            )
        );
        Assert.False(
            MemoryTimerSelection.TryMatchStoredClock(
                BitConverter.GetBytes(80f),
                MemoryTimerKind.FloatSeconds,
                600,
                out _
            )
        );
    }
}
