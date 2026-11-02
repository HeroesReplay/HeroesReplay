using System;
using System.Collections.Generic;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using HeroesReplay.Core.Clips;
using Xunit;

namespace HeroesReplay.Tests.Unit.Clips;

/// <summary>
/// A clip is cut from the recording, so it is only cut when the camera the spectator followed was
/// there. Replay 66406295 (Cursed Hollow, 2026-10-28): Raynor's team stole the boss at 13:25 while
/// the camera followed The Lost Vikings across the map, and the cut clip showed the Vikings.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClipCameraTests
{
    private const int Vikings = 1;
    private const int Raynor = 6;
    private const int Whitemane = 7;

    private static readonly MapPoint Pit = new(99, 62);

    [Fact]
    public void AStealWhileTheCameraFollowsAHeroAcrossTheMap_IsNotOnCamera()
    {
        var positions = new Positions();
        positions.Stand(Vikings, new MapPoint(200, 120), 770, 811);
        positions.Stand(Raynor, new MapPoint(100, 64), 770, 811);

        IReadOnlyDictionary<TimeSpan, Focus> camera = Camera((770, 811, Vikings));

        Assert.False(ClipCamera.Shows(Steal(), camera, positions, new ClipSettings()));
        Assert.Equal(new ClipCoverage(0, 36, false), Coverage(camera, positions));
    }

    [Fact]
    public void AStealWithTheCameraOnAHeroAtThePit_IsOnCamera()
    {
        var positions = new Positions();
        positions.Stand(Whitemane, new MapPoint(103, 60), 770, 811);

        IReadOnlyDictionary<TimeSpan, Focus> camera = Camera((770, 811, Whitemane));

        Assert.True(ClipCamera.Shows(Steal(), camera, positions, new ClipSettings()));
    }

    [Fact]
    public void ACameraThatLeavesThePitBeforeTheCapture_MissesTheMoment()
    {
        var positions = new Positions();
        positions.Stand(Whitemane, new MapPoint(103, 60), 770, 811);
        positions.Stand(Vikings, new MapPoint(200, 120), 770, 811);

        IReadOnlyDictionary<TimeSpan, Focus> camera = Camera(
            (770, 800, Whitemane),
            (801, 811, Vikings)
        );

        ClipCoverage coverage = Coverage(camera, positions);
        Assert.True(coverage.Share >= 0.5);
        Assert.False(coverage.Moment);
        Assert.False(ClipCamera.Shows(Steal(), camera, positions, new ClipSettings()));
    }

    [Fact]
    public void ACameraAtThePitForTheCaptureOnly_ShowsTooLittleOfTheFight()
    {
        var positions = new Positions();
        positions.Stand(Whitemane, new MapPoint(103, 60), 770, 811);
        positions.Stand(Vikings, new MapPoint(200, 120), 770, 811);

        IReadOnlyDictionary<TimeSpan, Focus> camera = Camera(
            (770, 800, Vikings),
            (801, 811, Whitemane)
        );

        Assert.False(ClipCamera.Shows(Steal(), camera, positions, new ClipSettings()));
    }

    [Fact]
    public void AKillClip_IsOnCameraWhileTheCameraFollowsTheKiller_EvenWithoutPositions()
    {
        TeamKillClip triple = TripleKill(Raynor);

        Assert.True(ClipCamera.Shows(triple, Camera((590, 592, Raynor)), null, new ClipSettings()));
        Assert.False(
            ClipCamera.Shows(triple, Camera((590, 592, Vikings)), null, new ClipSettings())
        );
    }

    [Fact]
    public void AKillClip_IsOnCameraWhileTheCameraFollowsAHeroNextToTheKiller()
    {
        var positions = new Positions();
        positions.Stand(Raynor, new MapPoint(50, 50), 590, 592);
        positions.Stand(Whitemane, new MapPoint(58, 54), 590, 592);
        positions.Stand(Vikings, new MapPoint(150, 50), 590, 592);

        Assert.True(
            ClipCamera.Shows(
                TripleKill(Raynor),
                Camera((590, 592, Whitemane)),
                positions,
                new ClipSettings()
            )
        );
        Assert.False(
            ClipCamera.Shows(
                TripleKill(Raynor),
                Camera((590, 592, Vikings)),
                positions,
                new ClipSettings()
            )
        );
    }

    [Fact]
    public void WithoutACameraTimeline_EveryClipIsKept()
    {
        Assert.True(ClipCamera.Shows(Steal(), null, new Positions(), new ClipSettings()));
        Assert.True(
            ClipCamera.Shows(
                Steal(),
                new Dictionary<TimeSpan, Focus>(),
                new Positions(),
                new ClipSettings()
            )
        );
    }

    [Fact]
    public void TheRadius_IsASetting()
    {
        var positions = new Positions();
        positions.Stand(Whitemane, new MapPoint(119, 62), 770, 811);
        IReadOnlyDictionary<TimeSpan, Focus> camera = Camera((770, 811, Whitemane));

        Assert.False(ClipCamera.Shows(Steal(), camera, positions, new ClipSettings()));
        Assert.True(
            ClipCamera.Shows(Steal(), camera, positions, new ClipSettings { CameraRadius = 25 })
        );
    }

    [Fact]
    public void TheExporter_LeavesOutAClipTheCameraDidNotShow()
    {
        var positions = new Positions();
        positions.Stand(Vikings, new MapPoint(200, 120), 770, 811);
        Func<TeamKillClip, bool> shown = MatchClipExporter.OnCamera(
            Camera((590, 592, Vikings), (770, 811, Vikings)),
            positions,
            new ClipSettings(),
            null
        );

        Assert.False(shown(Steal()));
        Assert.True(shown(TripleKill(Vikings)));
    }

    /// <summary>The 13:25 steal of replay 66406295: the fight from 12:50, cut from 12:47 to 13:31.</summary>
    private static TeamKillClip Steal() =>
        new(
            BossFightClips.StealKind,
            "Raynor",
            770,
            805,
            767,
            811,
            "Raynor steals the boss for Red",
            Point: Pit
        );

    private static TeamKillClip TripleKill(int killer) =>
        new(
            TeamKillClips.TripleKillKind,
            "Raynor",
            590,
            592,
            578,
            600,
            "Raynor triple kill",
            Player: killer
        );

    private static ClipCoverage Coverage(
        IReadOnlyDictionary<TimeSpan, Focus> camera,
        IHeroPositions positions
    ) => ClipCamera.Coverage(Steal(), camera, positions, new ClipSettings());

    private static IReadOnlyDictionary<TimeSpan, Focus> Camera(
        params (int From, int To, int Player)[] shots
    )
    {
        var camera = new Dictionary<TimeSpan, Focus>();
        foreach ((int from, int to, int player) in shots)
        {
            for (int second = from; second <= to; second++)
            {
                camera[TimeSpan.FromSeconds(second)] = new Focus(
                    typeof(ClipCameraTests),
                    null,
                    null,
                    1,
                    "shot",
                    player
                );
            }
        }

        return camera;
    }

    private sealed class Positions : IHeroPositions
    {
        private readonly Dictionary<(int Player, int Second), MapPoint> points = new();

        public void Stand(int player, MapPoint point, int from, int to)
        {
            for (int second = from; second <= to; second++)
            {
                points[(player, second)] = point;
            }
        }

        public bool TryGet(int playerIndex, TimeSpan time, out MapPoint point) =>
            points.TryGetValue((playerIndex, (int)time.TotalSeconds), out point);
    }
}
