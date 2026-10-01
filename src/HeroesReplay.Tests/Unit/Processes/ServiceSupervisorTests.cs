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
        Assert.DoesNotContain("HEROESREPLAY_SERVICE_NONCE", script);
    }

    [Fact]
    public void PowerShellStartCommand_SetsTheReadyNonceWithoutHidingTheConsole()
    {
        string script = ServicesCommand.PowerShellStartCommand(
            @"C:\heroesreplay\heroesreplay.exe",
            "twitch connect",
            @"C:\logs\twitch.pid",
            "abc123",
            "twitch",
            "1.2.3"
        );

        Assert.Contains("$env:HEROESREPLAY_SERVICE_NONCE='abc123'", script);
        Assert.Contains("$env:HEROESREPLAY_SERVICE_ROLE='twitch'", script);
        Assert.Contains("$env:HEROESREPLAY_SERVICE_VERSION='1.2.3'", script);
        Assert.Contains("-WindowStyle Normal", script);
        Assert.DoesNotContain("-RedirectStandardOutput", script);
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
    public void StillRunning_DropsReusedPidWhenStartTimeOrPathDiffers()
    {
        DateTimeOffset started = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string exe = @"C:\heroesreplay\heroesreplay.exe";
        var records = new[]
        {
            new ServiceProcessRecord
            {
                Name = "spectate",
                Pid = 10,
                Arguments = "spectate heroesprofile",
                ExecutablePath = exe,
                StartedAt = started,
            },
            new ServiceProcessRecord
            {
                Name = "twitch",
                Pid = 11,
                Arguments = "twitch connect",
                ExecutablePath = exe,
                StartedAt = started,
            },
            new ServiceProcessRecord
            {
                Name = "download",
                Pid = 12,
                Arguments = "heroesprofile download",
                ExecutablePath = exe,
                StartedAt = started,
            },
        };

        List<ServiceProcessRecord> living = ServiceProcessPlan.StillRunning(
            records,
            pid => "heroesreplay",
            pid =>
                pid switch
                {
                    10 => new ServiceProcessProbe { ExecutablePath = exe, StartedAt = started },
                    11 => new ServiceProcessProbe
                    {
                        ExecutablePath = @"C:\other\heroesreplay.exe",
                        StartedAt = started,
                    },
                    _ => new ServiceProcessProbe
                    {
                        ExecutablePath = exe,
                        StartedAt = started.AddMinutes(5),
                    },
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
            var nonces = new List<string>();
            ServiceStartupHandshake handshake = ServiceStartupHandshake.ReadyNow();
            int code = ServiceSupervisor.Start(
                path,
                @"C:\heroesreplay\heroesreplay.exe",
                pid => null,
                (name, arguments) =>
                {
                    nonces.Add(handshake.Pending.Nonce);
                    args.Add(arguments);
                    return 200 + args.Count;
                },
                handshake: handshake
            );

            Assert.Equal(0, code);
            Assert.Equal(4, args.Count);
            Assert.Equal(4, nonces.Distinct().Count());
            ServiceLock saved = ServiceLockStore.TryLoad(path);
            Assert.Equal(4, saved.Processes.Count);
            Assert.Equal(201, saved.Processes[0].Pid);
            Assert.Equal("youtube uploader", saved.Processes[3].Arguments);
            Assert.Equal(@"C:\heroesreplay\heroesreplay.exe", saved.Processes[0].ExecutablePath);
            Assert.False(string.IsNullOrWhiteSpace(saved.Processes[0].Nonce));
            Assert.False(string.IsNullOrWhiteSpace(saved.Processes[0].Version));
            Assert.NotNull(saved.Processes[0].StartedAt);
            Assert.NotNull(saved.Processes[0].ReadyAt);
            Assert.NotNull(saved.Processes[0].HeartbeatAt);
            Assert.Equal(saved.Processes[0].Nonce, nonces[0]);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_MarksRolesReadyAfterEveryRoleIsReady()
    {
        string path = TempLock();
        try
        {
            var marks = new List<DateTimeOffset?>();
            int launched = 0;
            ServiceStartupHandshake handshake = ServiceStartupHandshake.ReadyNow();
            handshake.RolesReady = since =>
            {
                Assert.True(since == null || launched == 4);
                marks.Add(since);
            };
            DateTimeOffset before = DateTimeOffset.UtcNow;
            int code = ServiceSupervisor.Start(
                path,
                @"C:\heroesreplay\heroesreplay.exe",
                pid => null,
                (name, arguments) => 300 + ++launched,
                handshake: handshake
            );

            Assert.Equal(0, code);
            Assert.Equal(2, marks.Count);
            Assert.Null(marks[0]);
            Assert.NotNull(marks[1]);
            Assert.True(marks[1] >= before);
            Assert.Equal(TimeSpan.Zero, marks[1].Value.Offset);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_ClearsRolesReadyAndDoesNotMarkItWhenARoleFails()
    {
        string path = TempLock();
        try
        {
            var marks = new List<DateTimeOffset?>();
            ServiceStartupHandshake handshake = ServiceStartupHandshake.ReadyNow();
            handshake.RolesReady = marks.Add;
            handshake.StopStarted = _ => { };
            int code = ServiceSupervisor.Start(
                path,
                @"C:\heroesreplay\heroesreplay.exe",
                pid => null,
                (name, arguments) => name == "twitch" ? null : 400,
                handshake: handshake
            );

            Assert.Equal(1, code);
            Assert.Equal(new DateTimeOffset?[] { null }, marks);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_RollsBackWhenAProcessDoesNotStart()
    {
        string path = TempLock();
        try
        {
            var stopped = new List<int>();
            var messages = new List<string>();
            ServiceStartupHandshake handshake = ServiceStartupHandshake.ReadyNow();
            handshake.StopStarted = stopped.Add;
            handshake.Report = messages.Add;
            int code = ServiceSupervisor.Start(
                path,
                @"C:\heroesreplay\heroesreplay.exe",
                pid => null,
                (name, arguments) => name == "download" ? null : 7,
                handshake: handshake
            );

            Assert.Equal(1, code);
            Assert.Null(ServiceLockStore.TryLoad(path));
            Assert.Equal(new[] { 7, 7 }, stopped);
            Assert.Contains(
                messages,
                message => message.Contains("download") && message.Contains("Failed to start")
            );
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

    [Fact]
    public void Start_EnsuresDashboardBeforeChildProcesses()
    {
        string path = TempLock();
        try
        {
            var order = new List<string>();
            int code = ServiceSupervisor.Start(
                path,
                @"C:\heroesreplay\heroesreplay.exe",
                pid => null,
                (name, arguments) =>
                {
                    order.Add(arguments);
                    return 5;
                },
                () => order.Add("clear"),
                () => order.Add("dashboard"),
                ServiceStartupHandshake.ReadyNow()
            );

            Assert.Equal(0, code);
            Assert.Equal("clear", order[0]);
            Assert.Equal("dashboard", order[1]);
            Assert.Equal("spectate heroesprofile", order[2]);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_DoesNotEnsureDashboardWhenServicesAreAlreadyRunning()
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
            int ensures = 0;
            int code = ServiceSupervisor.Start(
                path,
                @"C:\heroesreplay\heroesreplay.exe",
                pid => pid == 40 ? "heroesreplay" : null,
                (name, arguments) => 1,
                clearStopFile: () => { },
                ensureDashboard: () => ensures++
            );

            Assert.Equal(1, code);
            Assert.Equal(0, ensures);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_ContinuesWhenDashboardEnsureThrows()
    {
        string path = TempLock();
        try
        {
            int starts = 0;
            int code = ServiceSupervisor.Start(
                path,
                @"C:\heroesreplay\heroesreplay.exe",
                pid => null,
                (name, arguments) =>
                {
                    starts++;
                    return 9;
                },
                ensureDashboard: () => throw new InvalidOperationException("no aspire"),
                handshake: ServiceStartupHandshake.ReadyNow()
            );

            Assert.Equal(0, code);
            Assert.Equal(4, starts);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_ChildExitBeforeReady_RollsBackAndDeletesTheLock()
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
                        new()
                        {
                            Name = "spectate",
                            Pid = 1,
                            Arguments = "spectate heroesprofile",
                        },
                    },
                }
            );
            var stopped = new List<int>();
            var messages = new List<string>();
            int starts = 0;
            int code = ServiceSupervisor.Start(
                path,
                Exe,
                pid => null,
                (name, arguments) =>
                {
                    starts++;
                    return 100 + starts;
                },
                handshake: new ServiceStartupHandshake
                {
                    TryReadReady = _ => null,
                    ReadyTimeout = TimeSpan.FromSeconds(30),
                    Wait = _ => throw new InvalidOperationException("should not wait after exit"),
                    StopStarted = stopped.Add,
                    Report = messages.Add,
                }
            );

            Assert.Equal(1, code);
            Assert.Equal(1, starts);
            Assert.Equal(new[] { 101 }, stopped);
            Assert.Null(ServiceLockStore.TryLoad(path));
            Assert.Contains(
                messages,
                message => message.Contains("spectate") && message.Contains("exited before ready")
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_ChildExitAfterReadyBeforeHeartbeat_Returns1()
    {
        string path = TempLock();
        try
        {
            var stopped = new List<int>();
            var messages = new List<string>();
            DateTimeOffset readyAt = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
            int code = ServiceSupervisor.Start(
                path,
                Exe,
                pid => null,
                (name, arguments) => 41,
                handshake: new ServiceStartupHandshake
                {
                    TryReadReady = record => new ServiceReadyReport
                    {
                        Role = record.Name,
                        Nonce = record.Nonce,
                        ReadyAt = readyAt,
                    },
                    ReadyTimeout = TimeSpan.FromSeconds(30),
                    Wait = _ => throw new InvalidOperationException("should not wait after exit"),
                    StopStarted = stopped.Add,
                    Report = messages.Add,
                }
            );

            Assert.Equal(1, code);
            Assert.Equal(
                1,
                ServiceChildHeartbeat.ExitCode(
                    new ServiceReadyReport { ReadyAt = readyAt },
                    processExited: true
                )
            );
            Assert.Equal(
                0,
                ServiceChildHeartbeat.ExitCode(
                    new ServiceReadyReport
                    {
                        ReadyAt = readyAt,
                        HeartbeatAt = readyAt.AddSeconds(1),
                    },
                    processExited: true
                )
            );
            Assert.Equal(
                0,
                ServiceChildHeartbeat.ExitCode(
                    new ServiceReadyReport { ReadyAt = readyAt },
                    processExited: false
                )
            );
            Assert.Equal(new[] { 41 }, stopped);
            Assert.Null(ServiceLockStore.TryLoad(path));
            Assert.Contains(
                messages,
                message =>
                    message.Contains("spectate") && message.Contains("exited before heartbeat")
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_TimesOutWhenChildNeverBecomesReady()
    {
        string path = TempLock();
        try
        {
            var stopped = new List<int>();
            var messages = new List<string>();
            int code = ServiceSupervisor.Start(
                path,
                Exe,
                pid => "heroesreplay",
                (name, arguments) => 5,
                handshake: new ServiceStartupHandshake
                {
                    TryReadReady = _ => null,
                    ReadyTimeout = TimeSpan.Zero,
                    Wait = _ => throw new InvalidOperationException("should not wait"),
                    StopStarted = stopped.Add,
                    Report = messages.Add,
                }
            );

            Assert.Equal(1, code);
            Assert.Equal(new[] { 5 }, stopped);
            Assert.Null(ServiceLockStore.TryLoad(path));
            Assert.Contains(
                messages,
                message => message.Contains("spectate") && message.Contains("ready timed out")
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_WrongPrivilege_FailsBeforeAnyChildStarts()
    {
        string path = TempLock();
        try
        {
            var messages = new List<string>();
            int starts = 0;
            ServiceStartupHandshake handshake = ServiceStartupHandshake.ReadyNow();
            handshake.Spectate = HealthySpectate();
            handshake.Spectate.PrivilegeOk = false;
            handshake.Report = messages.Add;
            int code = ServiceSupervisor.Start(
                path,
                Exe,
                pid => "heroesreplay",
                (name, arguments) =>
                {
                    starts++;
                    return 1;
                },
                handshake: handshake
            );

            Assert.Equal(1, code);
            Assert.Equal(0, starts);
            Assert.Null(ServiceLockStore.TryLoad(path));
            Assert.Contains(messages, message => message.Contains("privilege"));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_NullOcr_FailsBeforeAnyChildStarts()
    {
        string path = TempLock();
        try
        {
            var messages = new List<string>();
            int starts = 0;
            ServiceStartupHandshake handshake = ServiceStartupHandshake.ReadyNow();
            handshake.Spectate = HealthySpectate();
            handshake.Spectate.OcrResult = null;
            handshake.Report = messages.Add;
            int code = ServiceSupervisor.Start(
                path,
                Exe,
                pid => "heroesreplay",
                (name, arguments) =>
                {
                    starts++;
                    return 1;
                },
                handshake: handshake
            );

            Assert.Equal(1, code);
            Assert.Equal(0, starts);
            Assert.Null(ServiceLockStore.TryLoad(path));
            Assert.Contains(messages, message => message.Contains("OCR"));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_InvalidSecrets_RollsBackTheChildrenAlreadyStarted()
    {
        string path = TempLock();
        try
        {
            var stopped = new List<int>();
            var messages = new List<string>();
            int starts = 0;
            ServiceStartupHandshake handshake = ServiceStartupHandshake.ReadyNow();
            handshake.Twitch = new TwitchStartupFacts
            {
                TokenOk = false,
                ScopesOk = false,
                RewardsOk = false,
                PredictionsOk = false,
            };
            handshake.StopStarted = stopped.Add;
            handshake.Report = messages.Add;
            int code = ServiceSupervisor.Start(
                path,
                Exe,
                pid => null,
                (name, arguments) =>
                {
                    starts++;
                    return 70 + starts;
                },
                handshake: handshake
            );

            Assert.Equal(1, code);
            Assert.Equal(1, starts);
            Assert.Equal(new[] { 71 }, stopped);
            Assert.Null(ServiceLockStore.TryLoad(path));
            Assert.Contains(
                messages,
                message => message.Contains("twitch") && message.Contains("token")
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_LaterChildFailure_RollsBackAndDoesNotLeaveAHealthyLock()
    {
        string path = TempLock();
        try
        {
            var stopped = new List<int>();
            var messages = new List<string>();
            int next = 500;
            ServiceStartupHandshake handshake = ServiceStartupHandshake.ReadyNow();
            handshake.YouTube = new YouTubeStartupFacts
            {
                Enabled = true,
                ContextWritable = false,
                OAuthRequired = true,
                OAuthOk = false,
            };
            handshake.StopStarted = stopped.Add;
            handshake.Report = messages.Add;
            int code = ServiceSupervisor.Start(
                path,
                Exe,
                pid => "heroesreplay",
                (name, arguments) => next++,
                handshake: handshake
            );

            Assert.Equal(1, code);
            Assert.Equal(new[] { 502, 501, 500 }, stopped);
            Assert.Null(ServiceLockStore.TryLoad(path));
            Assert.Contains(
                messages,
                message => message.Contains("youtube") && message.Contains("context")
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_YouTubeDisabled_DoesNotRequireOAuth()
    {
        string path = TempLock();
        try
        {
            ServiceStartupHandshake handshake = ServiceStartupHandshake.ReadyNow();
            handshake.YouTube = new YouTubeStartupFacts
            {
                Enabled = false,
                ContextWritable = false,
                OAuthRequired = false,
                OAuthOk = false,
            };
            int starts = 0;
            int code = ServiceSupervisor.Start(
                path,
                Exe,
                pid => null,
                (name, arguments) =>
                {
                    starts++;
                    return 3;
                },
                handshake: handshake
            );

            Assert.Equal(0, code);
            Assert.Equal(4, starts);
            Assert.Equal(4, ServiceLockStore.TryLoad(path).Processes.Count);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_IgnoresReusedPidWithADifferentStartTime()
    {
        string path = TempLock();
        try
        {
            DateTimeOffset started = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            ServiceLockStore.Save(
                path,
                new ServiceLock
                {
                    StartedAt = started,
                    Processes = new List<ServiceProcessRecord>
                    {
                        new()
                        {
                            Name = "spectate",
                            Pid = 40,
                            Arguments = "spectate heroesprofile",
                            ExecutablePath = Exe,
                            StartedAt = started,
                        },
                    },
                }
            );
            int starts = 0;
            ServiceStartupHandshake handshake = ServiceStartupHandshake.ReadyNow();
            handshake.Probe = pid => new ServiceProcessProbe
            {
                ExecutablePath = Exe,
                StartedAt = started.AddHours(3),
            };
            int code = ServiceSupervisor.Start(
                path,
                Exe,
                pid => "heroesreplay",
                (name, arguments) =>
                {
                    starts++;
                    return 10 + starts;
                },
                handshake: handshake
            );

            Assert.Equal(0, code);
            Assert.Equal(4, starts);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_DoesNotKillReusedPidWhenPathDiffers()
    {
        string path = TempLock();
        try
        {
            DateTimeOffset started = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            ServiceLockStore.Save(
                path,
                new ServiceLock
                {
                    Processes = new List<ServiceProcessRecord>
                    {
                        new()
                        {
                            Name = "spectate",
                            Pid = 50,
                            Arguments = "spectate heroesprofile",
                            ExecutablePath = Exe,
                            StartedAt = started,
                        },
                    },
                }
            );
            var killed = new List<int>();
            int code = ServiceSupervisor.Stop(
                path,
                pid => "heroesreplay",
                killed.Add,
                requestGracefulStop: () => { },
                gracefulWait: TimeSpan.Zero,
                probeOrNull: pid => new ServiceProcessProbe
                {
                    ExecutablePath = @"C:\other\heroesreplay.exe",
                    StartedAt = started,
                }
            );

            Assert.Equal(0, code);
            Assert.Empty(killed);
            Assert.Null(ServiceLockStore.TryLoad(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    private const string Exe = @"C:\heroesreplay\heroesreplay.exe";

    private static SpectateStartupFacts HealthySpectate()
    {
        return new SpectateStartupFacts
        {
            LaunchPath = Exe,
            PrivilegeOk = true,
            OcrResult = new object(),
            CaptureOk = true,
            PathsOk = true,
            ObsOk = true,
        };
    }

    private static string TempLock() =>
        Path.Combine(Path.GetTempPath(), $"heroesreplay-services-{Guid.NewGuid():N}.json");
}
