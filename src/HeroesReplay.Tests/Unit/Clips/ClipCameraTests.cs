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

    private const int Qhira = 1;
    private const int Hanzo = 4;
    private const int Mei = 6;

    private static readonly MapPoint HanzoStands = new(100, 100);

    /// <summary>
    /// Replay 66698689 (Garden of Terror): a hero focus replay locked the camera on Hanzo while
    /// Qhira got a quadra kill. Qhira stayed within 15 units of Hanzo for most of it, so the old
    /// check cut the clip, but only the last blow had Qhira and the victim on Hanzo's screen.
    /// The offsets are the replay's, measured from Hanzo at each blow.
    /// </summary>
    [Fact]
    public void AQuadraKillAtTheEdgeOfALockedCamera_IsNotOnCamera()
    {
        Positions positions = QuadraKillAround(HanzoStands);
        IReadOnlyDictionary<TimeSpan, Focus> camera = Camera((440, 490, Hanzo));

        ClipCoverage coverage = ClipCamera.Coverage(
            QuadraKill(),
            camera,
            positions,
            new ClipSettings()
        );
        Assert.True(coverage.Share >= 0.5);
        Assert.True(coverage.Moment);
        Assert.Equal(1, coverage.BlowsShown);
        Assert.Equal(4, coverage.Blows);
        Assert.False(ClipCamera.Shows(QuadraKill(), camera, positions, new ClipSettings()));
    }

    [Fact]
    public void AQuadraKill_IsOnCameraWhileTheCameraFollowsTheKiller()
    {
        Positions positions = QuadraKillAround(HanzoStands);

        Assert.True(
            ClipCamera.Shows(QuadraKill(), Camera((440, 490, Qhira)), positions, new ClipSettings())
        );
    }

    [Fact]
    public void AQuadraKill_IsOnCameraWhenThreeBlowsAreOnTheFollowedHerosScreen()
    {
        Positions positions = QuadraKillAround(HanzoStands);
        // Mei stands in the middle of the first, second and last blows, never the third.
        positions.Stand(Mei, new MapPoint(104, 106), 440, 490);

        ClipCoverage coverage = ClipCamera.Coverage(
            QuadraKill(),
            Camera((440, 490, Mei)),
            positions,
            new ClipSettings()
        );
        Assert.Equal(3, coverage.BlowsShown);
        Assert.True(
            ClipCamera.Shows(QuadraKill(), Camera((440, 490, Mei)), positions, new ClipSettings())
        );
    }

    [Fact]
    public void TheBlowShare_IsASetting()
    {
        Positions positions = QuadraKillAround(HanzoStands);
        IReadOnlyDictionary<TimeSpan, Focus> camera = Camera((440, 490, Hanzo));

        Assert.True(
            ClipCamera.Shows(
                QuadraKill(),
                camera,
                positions,
                new ClipSettings { CameraBlowShare = 0.25 }
            )
        );
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(5, 4)]
    [InlineData(6, 5)]
    public void TheBlowsNeeded_AreTheShareRoundedUp(int blows, int needed) =>
        Assert.Equal(needed, ClipCamera.BlowsNeeded(blows, 0.75));

    [Theory]
    [InlineData(12, 0, true)]
    [InlineData(13, 0, false)]
    [InlineData(-12, 0, true)]
    [InlineData(0, 8, true)]
    [InlineData(0, 9, false)]
    [InlineData(0, -6, true)]
    [InlineData(0, -7, false)]
    public void TheScreen_ReachesFurtherAboveTheHeroThanBelow(int dx, int dy, bool onScreen) =>
        Assert.Equal(
            onScreen,
            ClipCamera.OnScreen(
                HanzoStands,
                new MapPoint(HanzoStands.X + dx, HanzoStands.Y + dy),
                new ClipSettings()
            )
        );

    [Fact]
    public void AVictimThatCannotBePlaced_IsJudgedByTheKillerAlone()
    {
        var positions = new Positions();
        positions.Stand(Qhira, new MapPoint(103, 102), 457, 458);
        positions.Stand(Hanzo, HanzoStands, 457, 458);

        Assert.True(
            ClipCamera.ShowsBlow(
                Qhira,
                new TeamKillBlow(458, "Rehgar", 7),
                Camera((458, 458, Hanzo)),
                positions,
                new ClipSettings()
            )
        );
    }

    /// <summary>
    /// Qhira's four blows from replay 66698689, as (second, killer offset, victim offset, victim)
    /// from Hanzo; between blows Qhira stands near Hanzo.
    /// </summary>
    private static Positions QuadraKillAround(MapPoint hanzo)
    {
        var positions = new Positions();
        positions.Stand(Hanzo, hanzo, 440, 490);
        positions.Stand(Qhira, new MapPoint(hanzo.X + 6, hanzo.Y + 4), 440, 490);
        (int Second, int Kx, int Ky, int Vx, int Vy, int Victim)[] blows =
        {
            (458, 1, 8, 3, 9, 7),
            (465, 7, 6, 13, 8, 8),
            (469, 17, 20, 25, 27, 5),
            (478, -6, 4, 6, 1, 9),
        };
        foreach ((int second, int kx, int ky, int vx, int vy, int victim) in blows)
        {
            positions.Stand(Qhira, new MapPoint(hanzo.X + kx, hanzo.Y + ky), second, second);
            positions.Stand(
                victim,
                new MapPoint(hanzo.X + vx, hanzo.Y + vy),
                second - 2,
                second - 1
            );
        }

        return positions;
    }

    private static TeamKillClip QuadraKill() =>
        new(
            TeamKillClips.TripleKillKind,
            "Qhira",
            458,
            478,
            446,
            486,
            "Qhira quadra kill",
            new[]
            {
                new TeamKillBlow(458, "Rehgar", 7),
                new TeamKillBlow(465, "Genji", 8),
                new TeamKillBlow(469, "Greymane", 5),
                new TeamKillBlow(478, "Hogger", 9),
            },
            Player: Qhira
        );

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
