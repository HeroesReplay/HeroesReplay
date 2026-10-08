using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary>
/// #381: which heroesreplay processes <c>services stop</c> counts as a hand-started spectate of
/// this install, and which it must leave alone.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class UnrecordedSpectatesTests
{
    private const string Install =
        @"C:\heroesreplay\HeroesReplay\src\HeroesReplay.CLI\bin\Release\heroesreplay.exe";
    private const string OtherInstall = @"C:\heroesreplay\app\heroesreplay.exe";
    private static readonly DateTimeOffset Started = new(2026, 10, 8, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Find_SplitsThisInstallsSpectateFromAnotherInstalls()
    {
        var table = new[]
        {
            Entry(101, "heroesreplay.exe", Install),
            Entry(102, "heroesreplay.exe", OtherInstall),
            Entry(103, "heroesreplay.exe", null),
        };
        var lines = new Dictionary<int, string>
        {
            [101] = $"\"{Install}\" spectate file --path \"C:\\heroesreplay\\My Replays\"",
            [102] = $"\"{OtherInstall}\" spectate heroesprofile",
            [103] = "heroesreplay spectate file",
        };

        UnrecordedSpectates found = UnrecordedSpectates.Find(
            table,
            pid => lines.GetValueOrDefault(pid),
            Install,
            selfPid: 100,
            recordedPids: Array.Empty<int>()
        );

        ServiceProcessRecord ours = Assert.Single(found.ThisInstall);
        Assert.Equal(101, ours.Pid);
        Assert.Equal("spectate", ours.Name);
        Assert.Equal(Install, ours.ExecutablePath);
        Assert.Equal(Started, ours.StartedAt);
        Assert.Equal(@"spectate file --path C:\heroesreplay\My Replays", ours.Arguments);
        // Another path, and a path that could not be read, are never this install.
        Assert.Equal(new[] { 102, 103 }, found.OtherInstalls.Select(record => record.Pid));
    }

    [Fact]
    public void Find_SkipsItselfRecordedRolesOtherCommandsAndOtherProcesses()
    {
        var table = new[]
        {
            Entry(100, "heroesreplay.exe", Install),
            Entry(104, "heroesreplay.exe", Install),
            Entry(105, "heroesreplay.exe", Install),
            Entry(106, "heroesreplay.exe", Install),
            Entry(107, "HeroesOfTheStorm_x64.exe", Install),
            Entry(108, "heroesreplay.exe", Install),
            Entry(109, "dotnet.exe", @"C:\Program Files\dotnet\dotnet.exe"),
        };
        var lines = new Dictionary<int, string>
        {
            [100] = $"\"{Install}\" spectate file",
            [104] = $"\"{Install}\" spectate heroesprofile",
            [105] = $"\"{Install}\" mcp",
            [106] = $"\"{Install}\" services supervise",
            [107] = "HeroesOfTheStorm_x64.exe spectate",
            [109] = "dotnet heroesreplay.dll spectate file",
        };

        UnrecordedSpectates found = UnrecordedSpectates.Find(
            table,
            pid => lines.GetValueOrDefault(pid),
            Install,
            selfPid: 100,
            recordedPids: new[] { 104 }
        );

        // 100 is the stop itself, 104 is recorded, 105 and 106 are not spectate, 107 and 109 are
        // not heroesreplay, and 108's command line could not be read.
        Assert.Empty(found.ThisInstall);
        Assert.Empty(found.OtherInstalls);
    }

    [Fact]
    public void Find_ComparesTheInstallPathCaseInsensitively()
    {
        UnrecordedSpectates found = UnrecordedSpectates.Find(
            new[] { Entry(110, "HEROESREPLAY.EXE", Install.ToUpperInvariant()) },
            _ => "heroesreplay.exe Spectate file",
            Install,
            selfPid: 1,
            recordedPids: null
        );

        Assert.Equal(110, Assert.Single(found.ThisInstall).Pid);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("heroesreplay.exe", "")]
    [InlineData("heroesreplay.exe spectate file", "spectate|file")]
    [InlineData(
        "\"C:\\Program Files\\HR\\heroesreplay.exe\"  spectate   file --path \"C:\\a b\\c\"",
        "spectate|file|--path|C:\\a b\\c"
    )]
    public void Arguments_DropsTheExecutableAndSplitsOutsideQuotes(
        string commandLine,
        string expected
    )
    {
        Assert.Equal(expected, string.Join("|", ProcessCommandLine.Arguments(commandLine)));
    }

    [Fact]
    public void TryRead_ReadsThisProcessesCommandLine()
    {
        string line = ProcessCommandLine.TryRead(Environment.ProcessId);

        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.Null(ProcessCommandLine.TryRead(0));
    }

    private static ProcessTableEntry Entry(int pid, string name, string path) =>
        new(pid, 1, name, path, Started);
}
