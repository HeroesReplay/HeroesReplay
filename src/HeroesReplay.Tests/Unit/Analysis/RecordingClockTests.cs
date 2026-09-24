using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Services.Analysis;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class RecordingClockTests
{
    [Fact]
    public void TryFileSeconds_UsesTheFirstRecordingTimeThatReachedTheHud()
    {
        var clock = new RecordingClock();
        clock.Observe(TimeSpan.FromSeconds(80), TimeSpan.FromSeconds(10));
        clock.Observe(TimeSpan.FromSeconds(88), TimeSpan.FromSeconds(20));
        clock.Observe(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(70));

        Assert.True(clock.TryFileSeconds(88, out double start));
        Assert.Equal(20, start);
        Assert.True(clock.TryFileSeconds(120, out double end));
        Assert.Equal(70, end);
        Assert.False(clock.TryFileSeconds(200, out _));
    }

    [Fact]
    public void TryFileSeconds_KeepsTheEarlierFileTimeAfterARewind()
    {
        var clock = new RecordingClock();
        clock.Observe(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(40));
        clock.Observe(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(90));

        Assert.True(clock.TryFileSeconds(88, out double file));
        Assert.Equal(40, file);
    }

    [Fact]
    public void Ready_WritesFileTimesOnlyAfterBothEndsWereSeen()
    {
        var clock = new RecordingClock();
        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(
            new[]
            {
                new TeamKillDeath(100, "Li-Ming", "Artanis"),
                new TeamKillDeath(103, "Li-Ming", "Butcher"),
                new TeamKillDeath(106, "Li-Ming", "Chromie"),
                new TeamKillDeath(109, "Li-Ming", "Diablo"),
                new TeamKillDeath(112, "Li-Ming", "E.T.C."),
            }
        );
        TeamKillClip pentakill = clips.Single(clip => clip.Kind == TeamKillClips.PentakillKind);

        Assert.Empty(MatchClipList.Ready(7, clips, clock));

        clock.Observe(TimeSpan.FromSeconds(pentakill.HudStartSecond), TimeSpan.FromSeconds(15));
        Assert.Empty(MatchClipList.Ready(7, clips, clock));

        clock.Observe(TimeSpan.FromSeconds(pentakill.HudEndSecond), TimeSpan.FromSeconds(47));
        IReadOnlyList<MatchClipEntry> ready = MatchClipList.Ready(7, clips, clock);

        Assert.Equal(2, ready.Count);
        Assert.All(ready, entry => Assert.Equal(7, entry.ReplayId));
        Assert.Contains(
            ready,
            entry =>
                entry.Kind == TeamKillClips.PentakillKind
                && entry.FileStartSeconds == 15
                && entry.FileEndSeconds == 47
        );
        Assert.Contains(ready, entry => entry.File == "pentakill-Li-Ming-100.mp4");
    }
}
