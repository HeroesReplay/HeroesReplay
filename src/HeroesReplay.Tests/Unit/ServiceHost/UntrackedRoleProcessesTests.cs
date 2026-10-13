using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary>
/// #397: the role processes of this install that services.json does not track, which a restart
/// takes over or stops instead of starting the role a second time.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class UntrackedRoleProcessesTests
{
    private const string Install = @"C:\heroesreplay\app\heroesreplay.exe";
    private const string OtherInstall = @"C:\heroesreplay\app.previous\heroesreplay.exe";
    private static readonly DateTimeOffset Started = new(2026, 10, 9, 6, 24, 16, TimeSpan.Zero);

    [Fact]
    public void Find_PicksThisInstallsProcessesRunningExactlyTheRolesCommand()
    {
        var table = new[]
        {
            Entry(21960, "heroesreplay.exe", Install),
            Entry(21961, "heroesreplay.exe", OtherInstall),
            Entry(21962, "heroesreplay.exe", null),
            Entry(21963, "heroesreplay.exe", Install),
            Entry(21964, "heroesreplay.exe", Install),
            Entry(21965, "heroesreplay.exe", Install),
            Entry(21966, "heroesreplay.exe", Install),
            Entry(21967, "powershell.exe", Install),
            Entry(9999, "heroesreplay.exe", Install),
        };
        var lines = new Dictionary<int, string>
        {
            [21960] = $"\"{Install}\" SPECTATE HeroesProfile",
            [21961] = $"\"{OtherInstall}\" spectate heroesprofile",
            [21962] = "heroesreplay spectate heroesprofile",
            [21963] = $"\"{Install}\" spectate file --path C:\\heroesreplay\\Replays",
            [21964] = $"\"{Install}\" heroesprofile download",
            [21965] = $"\"{Install}\" spectate heroesprofile",
            [21967] = $"\"{Install}\" spectate heroesprofile",
            [9999] = $"\"{Install}\" spectate heroesprofile",
        };

        IReadOnlyList<UntrackedRoleProcess> found = UntrackedRoleProcesses.Find(
            "spectate",
            table,
            pid => lines.GetValueOrDefault(pid),
            Install,
            selfPid: 9999,
            trackedPids: new[] { 21965 },
            (pid, role) =>
                pid == 21960
                    ? new ServiceReadyReport
                    {
                        Role = role,
                        Pid = pid,
                        Nonce = "abc123",
                        Readiness = ServiceReadiness.Ready,
                    }
                    : null
        );

        // Another install, an unreadable path or command line, another command, the tracked
        // role, another process name, and the supervisor itself are never counted.
        UntrackedRoleProcess spectate = Assert.Single(found);
        Assert.Equal(21960, spectate.Process.Pid);
        Assert.Equal("spectate", spectate.Process.Name);
        Assert.Equal("spectate heroesprofile", spectate.Process.Arguments);
        Assert.Equal(Install, spectate.Process.ExecutablePath);
        Assert.Equal(Started, spectate.Process.StartedAt);
        Assert.Equal("abc123", spectate.Heartbeat.Nonce);
        Assert.True(spectate.Adoptable);

        ServiceProcessRecord adopted = spectate.Adopt();
        Assert.Equal(21960, adopted.Pid);
        Assert.Equal("abc123", adopted.Nonce);
        Assert.Equal(Started, adopted.StartedAt);
    }

    [Fact]
    public void Adoptable_NeedsAReadyHeartbeatWithAUsableNonce()
    {
        var process = new ServiceProcessRecord { Name = "download", Pid = 7 };

        Assert.False(new UntrackedRoleProcess(process, null).Adoptable);
        Assert.False(
            new UntrackedRoleProcess(
                process,
                new ServiceReadyReport { Nonce = "abc", Readiness = ServiceReadiness.Stopping }
            ).Adoptable
        );
        Assert.False(
            new UntrackedRoleProcess(
                process,
                new ServiceReadyReport { Nonce = "../x", Readiness = ServiceReadiness.Ready }
            ).Adoptable
        );
        Assert.True(
            new UntrackedRoleProcess(
                process,
                new ServiceReadyReport { Nonce = "abc", Readiness = ServiceReadiness.Ready }
            ).Adoptable
        );
    }

    [Fact]
    public void Find_KnowsNoUnknownRole()
    {
        Assert.Empty(
            UntrackedRoleProcesses.Find(
                "mcp",
                new[] { Entry(5, "heroesreplay.exe", Install) },
                _ => $"\"{Install}\" mcp",
                Install,
                selfPid: 1,
                trackedPids: Array.Empty<int>()
            )
        );
    }

    [Fact]
    public void FindByPid_ReadsTheReadyFileThatPidWroteForTheRole()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-ready-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            DateTimeOffset now = Started.AddHours(3);
            ServiceReadyFile.Report(
                Report("spectate", 21960, "old", now.AddMinutes(-5)),
                directory
            );
            ServiceReadyFile.Report(Report("spectate", 21960, "new", now), directory);
            ServiceReadyFile.Report(
                Report("download", 21960, "role", now.AddMinutes(1)),
                directory
            );
            ServiceReadyFile.Report(
                Report("spectate", 4242, "other", now.AddMinutes(1)),
                directory
            );
            // A file whose name is not its nonce is not a ready file the supervisor can watch.
            File.WriteAllText(
                Path.Combine(directory, "renamed.json"),
                File.ReadAllText(Path.Combine(directory, "other.json")).Replace("4242", "21960")
            );

            ServiceReadyReport found = ServiceReadyFile.FindByPid(21960, "spectate", directory);

            Assert.Equal("new", found.Nonce);
            Assert.Null(ServiceReadyFile.FindByPid(1, "spectate", directory));
            Assert.Null(ServiceReadyFile.FindByPid(21960, "twitch", directory));
            Assert.Null(
                ServiceReadyFile.FindByPid(21960, "spectate", Path.Combine(directory, "missing"))
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ServiceReadyReport Report(
        string role,
        int pid,
        string nonce,
        DateTimeOffset beat
    ) =>
        new()
        {
            Role = role,
            Pid = pid,
            Nonce = nonce,
            Readiness = ServiceReadiness.Ready,
            ReadyAt = beat.AddMinutes(-1),
            HeartbeatAt = beat,
        };

    private static ProcessTableEntry Entry(int pid, string name, string path) =>
        new(pid, 4, name, path, path == null ? null : Started);
}
