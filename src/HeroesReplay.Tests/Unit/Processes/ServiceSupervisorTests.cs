using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.Services.Processes;
using Xunit;

namespace HeroesReplay.Tests.Unit.Processes;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceSupervisorTests
{
    [Fact]
    public void PowerShellStartCommand_QuotesPathAndSplitsArguments()
    {
        string script = ServicesCommand.PowerShellStartCommand(
            @"C:\heroes replay\heroesreplay.exe",
            "spectate heroesprofile",
            @"C:\logs\spectate.pid"
        );

        Assert.Contains(@"'C:\heroes replay\heroesreplay.exe'", script);
        Assert.Contains("-ArgumentList 'spectate','heroesprofile'", script);
        Assert.Contains("-WindowStyle Normal", script);
        Assert.DoesNotContain("-WindowStyle Hidden", script);
        Assert.DoesNotContain("-RedirectStandardOutput", script);
        Assert.Contains(@"Set-Content -LiteralPath 'C:\logs\spectate.pid'", script);
        Assert.DoesNotContain("spectate heroesprofile", script);
    }

    [Fact]
    public void ParseProcessId_IgnoresProgressClixml()
    {
        string stdout = "#< CLIXML\r\n<Objs />\r\n11532\r\n";
        Assert.Equal(11532, ServicesCommand.ParseProcessId(stdout));
        Assert.Null(ServicesCommand.ParseProcessId("#< CLIXML"));
    }

    [Fact]
    public void Plan_StartsFourSeparateProcesses()
    {
        Assert.Equal(
            new[]
            {
                "spectate heroesprofile",
                "twitch connect",
                "heroesprofile download",
                "youtube uploader",
            },
            ServiceProcessPlan.All.Select(item => item.Arguments).ToArray()
        );
    }

    [Theory]
    [InlineData("heroesreplay", true)]
    [InlineData("HeroesReplay.exe", true)]
    [InlineData("notepad", false)]
    [InlineData(null, false)]
    public void IsHeroesReplay_MatchesThisExecutable(string name, bool expected)
    {
        Assert.Equal(expected, ServiceProcessPlan.IsHeroesReplay(name));
    }

    [Fact]
    public void StillRunning_DropsDeadAndReusedPids()
    {
        var records = new[]
        {
            new ServiceProcessRecord
            {
                Name = "spectate",
                Pid = 10,
                Arguments = "spectate heroesprofile",
            },
            new ServiceProcessRecord
            {
                Name = "twitch",
                Pid = 11,
                Arguments = "twitch connect",
            },
            new ServiceProcessRecord
            {
                Name = "download",
                Pid = 12,
                Arguments = "heroesprofile download",
            },
        };

        List<ServiceProcessRecord> living = ServiceProcessPlan.StillRunning(
            records,
            pid =>
                pid switch
                {
                    10 => "heroesreplay",
                    11 => "notepad",
                    _ => null,
                }
        );

        ServiceProcessRecord only = Assert.Single(living);
        Assert.Equal(10, only.Pid);
    }

    [Fact]
    public void Start_RefusesWhenServicesAreAlreadyRunning()
    {
        string path = TempLock();
        try
        {
            ServiceLockStore.Save(
                path,
                new ServiceLock
                {
                    StartedAt = DateTimeOffset.UtcNow,
                    Processes = new List<ServiceProcessRecord>
                    {
                        new()
                        {
                            Name = "spectate",
                            Pid = 40,
                            Arguments = "spectate heroesprofile",
                        },
                    },
                }
            );
            int starts = 0;
            int code = ServiceSupervisor.Start(
                path,
                @"C:\heroesreplay\heroesreplay.exe",
                pid => pid == 40 ? "heroesreplay" : null,
                (name, arguments) =>
                {
                    starts++;
                    return 1;
                }
            );

            Assert.Equal(1, code);
            Assert.Equal(0, starts);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_RecordsEveryProcess()
    {
        string path = TempLock();
        try
        {
            var args = new List<string>();
            int code = ServiceSupervisor.Start(
                path,
                @"C:\heroesreplay\heroesreplay.exe",
                pid => null,
                (name, arguments) =>
                {
                    args.Add(arguments);
                    return 200 + args.Count;
                }
            );

            Assert.Equal(0, code);
            Assert.Equal(4, args.Count);
            ServiceLock saved = ServiceLockStore.TryLoad(path);
            Assert.Equal(4, saved.Processes.Count);
            Assert.Equal(201, saved.Processes[0].Pid);
            Assert.Equal("youtube uploader", saved.Processes[3].Arguments);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_SavesPartialWhenAProcessFails()
    {
        string path = TempLock();
        try
        {
            int code = ServiceSupervisor.Start(
                path,
                @"C:\heroesreplay\heroesreplay.exe",
                pid => null,
                (name, arguments) => name == "download" ? null : 7
            );

            Assert.Equal(1, code);
            ServiceLock saved = ServiceLockStore.TryLoad(path);
            Assert.Equal(new[] { "spectate", "twitch" }, saved.Processes.Select(p => p.Name));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_KillsOnlyLivingHeroesReplayPids()
    {
        string path = TempLock();
        try
        {
            ServiceLockStore.Save(
                path,
                new ServiceLock
                {
                    Processes = new List<ServiceProcessRecord>
                    {
                        new() { Name = "spectate", Pid = 50 },
                        new() { Name = "twitch", Pid = 51 },
                    },
                }
            );
            var killed = new List<int>();
            int code = ServiceSupervisor.Stop(
                path,
                pid => pid == 50 ? "heroesreplay" : "explorer",
                killed.Add,
                requestGracefulStop: () => { },
                gracefulWait: TimeSpan.Zero
            );

            Assert.Equal(0, code);
            Assert.Equal(new[] { 50 }, killed);
            Assert.Null(ServiceLockStore.TryLoad(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_WaitsForGracefulExitBeforeKilling()
    {
        string path = TempLock();
        try
        {
            ServiceLockStore.Save(
                path,
                new ServiceLock
                {
                    Processes = new List<ServiceProcessRecord>
                    {
                        new() { Name = "spectate", Pid = 70 },
                    },
                }
            );
            int polls = 0;
            var killed = new List<int>();
            bool requested = false;
            int code = ServiceSupervisor.Stop(
                path,
                pid =>
                {
                    polls++;
                    return polls < 3 ? "heroesreplay" : null;
                },
                killed.Add,
                () => requested = true,
                TimeSpan.FromSeconds(5),
                _ => { }
            );

            Assert.Equal(0, code);
            Assert.True(requested);
            Assert.Empty(killed);
            Assert.Null(ServiceLockStore.TryLoad(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void StopFile_CancelsWhenTheFileAppears()
    {
        string path = Path.Combine(Path.GetTempPath(), $"heroesreplay-stop-{Guid.NewGuid():N}");
        try
        {
            using var idle = ServiceStopFile.Link(CancellationToken.None, path);
            Assert.False(idle.Token.WaitHandle.WaitOne(300));

            ServiceStopFile.Request(path);
            Assert.True(idle.Token.WaitHandle.WaitOne(2000));
        }
        finally
        {
            ServiceStopFile.Clear(path);
        }
    }

    [Fact]
    public void Stop_ClosesTheGameWhenSpectateWasRunning()
    {
        string path = TempLock();
        try
        {
            ServiceLockStore.Save(
                path,
                new ServiceLock
                {
                    Processes = new List<ServiceProcessRecord>
                    {
                        new() { Name = "spectate", Pid = 80 },
                    },
                }
            );
            bool closedGame = false;
            ServiceSupervisor.Stop(
                path,
                pid => null,
                _ => { },
                requestGracefulStop: () => { },
                gracefulWait: TimeSpan.Zero,
                wait: _ => { },
                clearStopFile: () => { },
                stopSpectatedGame: () => closedGame = true
            );
            Assert.True(closedGame);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Status_NotRunning_IsSuccess()
    {
        string path = TempLock();
        try
        {
            int code = ServiceSupervisor.Status(path, pid => null, spectator: null);
            Assert.Equal(0, code);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Status_Partial_IsFailure()
    {
        string path = TempLock();
        try
        {
            ServiceLockStore.Save(
                path,
                new ServiceLock
                {
                    Processes = new List<ServiceProcessRecord>
                    {
                        new() { Name = "spectate", Pid = 60 },
                        new() { Name = "twitch", Pid = 61 },
                    },
                }
            );
            int code = ServiceSupervisor.Status(
                path,
                pid => pid == 60 ? "heroesreplay" : null,
                spectator: null
            );
            Assert.Equal(1, code);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    private static string TempLock() =>
        Path.Combine(Path.GetTempPath(), $"heroesreplay-services-{Guid.NewGuid():N}.json");
}
