using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analysis;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class AbilityDetectorTests
{
    private readonly AbilityDetector detector = new();

    [Fact]
    public void IsBuildInRange_HonorsInclusiveStartAndExclusiveEnd()
    {
        Assert.True(
            AbilityDetector.IsBuildInRange(98025, greaterEqualBuild: 68740, lessThanBuild: null)
        );
        Assert.True(
            AbilityDetector.IsBuildInRange(68739, greaterEqualBuild: null, lessThanBuild: 68740)
        );
        Assert.False(
            AbilityDetector.IsBuildInRange(68740, greaterEqualBuild: null, lessThanBuild: 68740)
        );
        Assert.False(
            AbilityDetector.IsBuildInRange(68739, greaterEqualBuild: 68740, lessThanBuild: null)
        );
        Assert.False(
            AbilityDetector.IsBuildInRange(70000, greaterEqualBuild: 70682, lessThanBuild: 68740)
        );
        Assert.True(
            AbilityDetector.IsBuildInRange(100000, greaterEqualBuild: 79033, lessThanBuild: null)
        );
    }

    [Fact]
    public void IsAbility_MatchesLinkAndCommandInsideTheBuildWindow()
    {
        var detection = new AbilityDetection
        {
            CmdIndex = 3,
            AbilityBuilds = new[]
            {
                new AbilityBuild { AbilityLink = 19, LessThanBuild = 68740 },
                new AbilityBuild { AbilityLink = 22, GreaterEqualBuild = 68740 },
            },
        };

        Assert.True(detector.IsAbility(Replay(68739), Command(19, 3), detection));
        Assert.False(detector.IsAbility(Replay(68740), Command(19, 3), detection));
        Assert.False(detector.IsAbility(Replay(68739), Command(22, 3), detection));
        Assert.True(detector.IsAbility(Replay(98025), Command(22, 3), detection));
        Assert.False(detector.IsAbility(Replay(98025), Command(22, 4), detection));
        Assert.False(detector.IsAbility(Replay(98025), Command(19, 3), detection));
    }

    [Fact]
    public void IsAbility_RejectsMissingInputsAndSkipsCommandWhenUnset()
    {
        var hearth = new AbilityDetection
        {
            AbilityBuilds = new[]
            {
                new AbilityBuild { AbilityLink = 115, GreaterEqualBuild = 79033 },
            },
        };

        Assert.False(detector.IsAbility(null, Command(115, 0), hearth));
        Assert.False(detector.IsAbility(Replay(98025), null, hearth));
        Assert.False(detector.IsAbility(Replay(98025), Command(115, 0), null));
        Assert.False(
            detector.IsAbility(
                Replay(98025),
                Command(115, 0),
                new AbilityDetection { AbilityBuilds = null }
            )
        );
        Assert.True(detector.IsAbility(Replay(98025), Command(115, 0), hearth));
        Assert.True(detector.IsAbility(Replay(98025), Command(115, 2), hearth));
        Assert.False(detector.IsAbility(Replay(79032), Command(115, 0), hearth));
        Assert.False(detector.IsAbility(Replay(98025), Command(22, 0), hearth));
    }

    [Fact]
    public void AppSettings_AbilityWindowsMatchShippedBuilds()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        Assert.True(File.Exists(path), $"expected {path} to be copied to the test output.");

        IConfigurationRoot configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        AbilityDetectionSettings settings = configuration
            .GetSection("AbilityDetection")
            .Get<AbilityDetectionSettings>();
        Assert.NotNull(settings);
        Assert.Equal(4, settings.Taunt.CmdIndex);
        Assert.Equal(3, settings.Dance.CmdIndex);
        Assert.Null(settings.Hearth.CmdIndex);

        var hearthLinks = new (int build, int link)[]
        {
            (61871, 200),
            (61872, 119),
            (68739, 119),
            (68740, 116),
            (70681, 116),
            (70682, 112),
            (77524, 112),
            (77525, 114),
            (79032, 114),
            (79033, 115),
            (98025, 115),
            (99999, 115),
            (100000, 115),
        };

        foreach ((int build, int link) in hearthLinks)
        {
            List<int> hits = settings
                .Hearth.AbilityBuilds.Where(entry =>
                    detector.IsAbility(
                        Replay(build),
                        Command(entry.AbilityLink, 0),
                        settings.Hearth
                    )
                )
                .Select(entry => entry.AbilityLink)
                .ToList();
            Assert.Equal(new[] { link }, hits);
        }

        foreach (AbilityBuild entry in settings.Hearth.AbilityBuilds)
        {
            if (entry.GreaterEqualBuild.HasValue && entry.LessThanBuild.HasValue)
            {
                Assert.True(
                    entry.GreaterEqualBuild.Value < entry.LessThanBuild.Value,
                    $"hearth link {entry.AbilityLink} can never match a replay build."
                );
            }
        }

        Assert.True(detector.IsAbility(Replay(68739), Command(19, 3), settings.Dance));
        Assert.True(detector.IsAbility(Replay(68739), Command(19, 4), settings.Taunt));
        Assert.False(detector.IsAbility(Replay(68739), Command(22, 3), settings.Dance));
        Assert.True(detector.IsAbility(Replay(98025), Command(22, 3), settings.Dance));
        Assert.False(detector.IsAbility(Replay(98025), Command(22, 3), settings.Taunt));
        Assert.True(detector.IsAbility(Replay(98025), Command(22, 4), settings.Taunt));
        Assert.False(detector.IsAbility(Replay(98025), Command(22, 4), settings.Dance));
        Assert.False(detector.IsAbility(Replay(98025), Command(22, 0), settings.Dance));
        Assert.False(detector.IsAbility(Replay(98025), Command(22, 0), settings.Taunt));
        Assert.True(detector.IsAbility(Replay(100000), Command(22, 3), settings.Dance));
        Assert.True(detector.IsAbility(Replay(98025), Command(115, 0), settings.Hearth));
        Assert.False(detector.IsAbility(Replay(98025), Command(200, 0), settings.Hearth));
    }

    private static Replay Replay(int build)
    {
        return new Replay { ReplayBuild = build };
    }

    private static GameEvent Command(int abilityLink, int cmdIndex)
    {
        return new GameEvent
        {
            eventType = GameEventType.CCmdEvent,
            data = new TrackerEventStructure
            {
                array = new[]
                {
                    null,
                    new TrackerEventStructure
                    {
                        array = new[]
                        {
                            new TrackerEventStructure { unsignedInt = (ulong)abilityLink },
                            new TrackerEventStructure { unsignedInt = (ulong)cmdIndex },
                        },
                    },
                },
            },
        };
    }
}
