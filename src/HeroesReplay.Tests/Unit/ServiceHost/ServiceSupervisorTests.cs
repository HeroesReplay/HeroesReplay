using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

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
            var processes = new FakeProcesses(50);
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.Zero);
            shutdown.ProcessNameOrNull = pid => pid == 51 ? "explorer" : processes.Name(pid);
            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(new[] { 50 }, processes.Killed);
            Assert.Equal(
                new[] { ServiceStopOutcome.Killed, ServiceStopOutcome.AlreadyExited },
                result.Roles.Select(role => role.Outcome).ToArray()
            );
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
            ServiceStopResult result = ServiceSupervisor.Stop(
                path,
                new ServiceShutdown
                {
                    ProcessNameOrNull = pid =>
                    {
                        polls++;
                        return polls < 3 ? "heroesreplay" : null;
                    },
                    Kill = killed.Add,
                    RequestGracefulStop = () => requested = true,
                    GracefulWait = TimeSpan.FromSeconds(5),
                    Wait = _ => { },
                }
            );

            Assert.Equal(0, result.ExitCode);
            Assert.True(requested);
            Assert.Empty(killed);
            Assert.Equal(ServiceStopOutcome.Graceful, Assert.Single(result.Roles).Outcome);
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
            using var idle = ServiceStopFile.Link(
                CancellationToken.None,
                path,
                TimeSpan.FromMilliseconds(10)
            );
            Assert.False(idle.Token.WaitHandle.WaitOne(300));

            ServiceStopFile.Request(path);
            // The watcher runs on the thread pool, which a busy machine can starve for a while:
            // wait as long as it takes a working watcher, and fail only a dead one (#331).
            Assert.True(idle.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(15)));
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
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.CloseGame = () => closedGame = true;
            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.True(closedGame);
            Assert.True(result.GameClosed);
            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_ClosesAnIdleSwitcherAfterTheGameAndNamesItOnTheGameLine()
    {
        // #359: Heroes pid 40020 closed, and HeroesSwitcher_x64 pid 40108 was left running.
        string path = TempLock();
        try
        {
            SaveRoles(path, 80);
            var steps = new List<string>();
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.CloseGame = () =>
            {
                steps.Add("game");
                return true;
            };
            shutdown.CloseIdleSwitchers = () =>
            {
                steps.Add("switchers");
                return Switchers(new SwitcherStop(40108, SwitcherStopOutcome.Closed));
            };

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(new[] { "game", "switchers" }, steps);
            Assert.Equal(0, result.ExitCode);
            Assert.True(result.GameClosed);
            Assert.Empty(result.Failures());
            Assert.Equal(
                "Heroes of the Storm: closed. HeroesSwitcher_x64 pid 40108: closed.",
                ServiceStopResult.DescribeGame(result.GameClosed.Value, result.Switchers)
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_FailsWhenAnIdleSwitcherIsStillRunning()
    {
        string path = TempLock();
        try
        {
            SaveRoles(path, 80);
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.CloseGame = () => true;
            shutdown.CloseIdleSwitchers = () =>
                Switchers(
                    new SwitcherStop(40108, SwitcherStopOutcome.StillRunning, "kill: AccessDenied")
                );

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(1, result.ExitCode);
            Assert.True(result.GameClosed);
            Assert.Contains("HeroesSwitcher_x64 pid 40108 is still running.", result.Failures());
            Assert.Equal(
                "Heroes of the Storm: closed. HeroesSwitcher_x64 pid 40108: still running (kill: AccessDenied).",
                ServiceStopResult.DescribeGame(true, result.Switchers)
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_ASwitcherWhoseHeroesChildRuns_IsLeftAndTheGameCountsAsRunning()
    {
        // The switcher started Heroes again after the game closed: that is a game, not an idle
        // switcher, so it is left and the stop fails on the game.
        string path = TempLock();
        try
        {
            SaveRoles(path, 80);
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.CloseGame = () => true;
            shutdown.CloseIdleSwitchers = () =>
                new SwitcherStopResult { LeftAlone = new[] { new SwitcherHandoff(40108, 40500) } };

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(1, result.ExitCode);
            Assert.False(result.GameClosed);
            Assert.Contains("Heroes of the Storm is still running.", result.Failures());
            Assert.Equal(
                "Heroes of the Storm: still running. HeroesSwitcher_x64 pid 40108: left running, its Heroes pid 40500 is still up.",
                ServiceStopResult.DescribeGame(false, result.Switchers)
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_WithoutSpectate_LeavesTheGameAndTheSwitchersAlone()
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
                        new() { Name = "twitch", Pid = 81 },
                    },
                }
            );
            bool touched = false;
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.CloseGame = () => touched = true;
            shutdown.CloseIdleSwitchers = () =>
            {
                touched = true;
                return Switchers();
            };

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.False(touched);
            Assert.Null(result.GameClosed);
            Assert.Null(result.Switchers);
            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_ASwitcherStepThatThrows_DoesNotFailTheStop()
    {
        string path = TempLock();
        try
        {
            SaveRoles(path, 80);
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.CloseGame = () => true;
            shutdown.CloseIdleSwitchers = () =>
                throw new InvalidOperationException("process table unreadable");

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(0, result.ExitCode);
            Assert.Null(result.Switchers);
            Assert.Equal(
                "Heroes of the Storm: closed.",
                ServiceStopResult.DescribeGame(true, result.Switchers)
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    private static SwitcherStopResult Switchers(params SwitcherStop[] stopped) =>
        new() { Stopped = stopped };

    /// <summary>
    /// #381: a `spectate file` started by hand, with no services.json. The stop still asks it to
    /// exit through the stop file, then closes the game, the idle switcher, and the recording it
    /// claimed, and services.json is not created.
    /// </summary>
    [Fact]
    public void Stop_WithoutServicesJson_StopsTheHandStartedSpectateAndClosesTheGame()
    {
        string path = TempLock();
        try
        {
            var processes = new FakeProcesses(90);
            var steps = new List<string>();
            IReadOnlyCollection<int> scanned = null;
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.FromSeconds(20));
            shutdown.FindUnrecordedRoles = recorded =>
            {
                scanned = recorded;
                return new UnrecordedRoles { ThisInstall = new[] { HandStarted(90) } };
            };
            shutdown.RequestGracefulStop = () =>
            {
                steps.Add("stop file");
                processes.Exit(90);
            };
            shutdown.GameRunning = () =>
            {
                // The found spectate ran the game: it is closed without asking.
                steps.Add("game running?");
                return false;
            };
            shutdown.CloseGame = () =>
            {
                steps.Add("game");
                return true;
            };
            shutdown.CloseIdleSwitchers = () =>
            {
                steps.Add("switchers");
                return Switchers(new SwitcherStop(40108, SwitcherStopOutcome.Closed));
            };
            shutdown.ReadStream = ServiceStreamCheck.NotRunning;
            shutdown.StopSpectateRecording = () =>
            {
                steps.Add("recording");
                return new OrphanRecordingCheck(OrphanRecordingState.Stopped, "stopped");
            };

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(scanned);
            Assert.Equal(new[] { "stop file", "game", "switchers", "recording" }, steps);
            Assert.Empty(processes.Killed);
            ServiceRoleStop role = Assert.Single(result.Roles);
            Assert.Equal(
                ("spectate", 90, ServiceStopOutcome.Graceful),
                (role.Name, role.Pid, role.Outcome)
            );
            Assert.Equal("spectate pid 90: graceful. Not in services.json.", role.Describe());
            Assert.True(result.GameClosed);
            Assert.Equal(OrphanRecordingState.Stopped, result.Recording.State);
            Assert.Equal("Services stopped.", result.Summary);
            Assert.Null(ServiceLockStore.TryLoad(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_WithoutServicesJson_KillsAHandStartedSpectateLeftAfterTheBudget()
    {
        string path = TempLock();
        try
        {
            var processes = new FakeProcesses(91);
            bool closedGame = false;
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.Zero);
            shutdown.FindUnrecordedRoles = _ => new UnrecordedRoles
            {
                ThisInstall = new[] { HandStarted(91) },
            };
            shutdown.CloseGame = () => closedGame = true;

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(new[] { 91 }, processes.Killed);
            Assert.Equal(ServiceStopOutcome.Killed, Assert.Single(result.Roles).Outcome);
            Assert.True(closedGame);
            Assert.Null(ServiceLockStore.TryLoad(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    /// <summary>
    /// A hand-started spectate that survives the kill fails the stop like a recorded role, leaves
    /// OBS alone, and is still not written to services.json: that file is only what
    /// services start launched.
    /// </summary>
    [Fact]
    public void Stop_HandStartedSpectateStillRunning_FailsWithoutRecordingIt()
    {
        string path = TempLock();
        try
        {
            var processes = new FakeProcesses(97);
            processes.Unkillable.Add(97);
            int obsReads = 0;
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.Zero);
            shutdown.FindUnrecordedRoles = _ => new UnrecordedRoles
            {
                ThisInstall = new[] { HandStarted(97) },
            };
            shutdown.CloseGame = () => true;
            shutdown.ReadStream = () =>
            {
                obsReads++;
                return ServiceStreamCheck.NotRunning();
            };
            shutdown.StopSpectateRecording = () =>
            {
                obsReads++;
                return new OrphanRecordingCheck(OrphanRecordingState.None, null);
            };

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(0, obsReads);
            ServiceRoleStop role = Assert.Single(result.Roles);
            Assert.Equal(ServiceStopOutcome.StillRunning, role.Outcome);
            Assert.Equal(
                "spectate pid 97: still running. Not in services.json. Kill failed: Access is denied.",
                role.Describe()
            );
            Assert.Contains("spectate pid 97 is still running.", result.Failures());
            Assert.Null(ServiceLockStore.TryLoad(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    /// <summary>#381: no services.json and nothing running: exit 0, nothing closed.</summary>
    [Fact]
    public void Stop_WithoutServicesJson_NothingRunning_SaysNothingToStop()
    {
        string path = TempLock();
        try
        {
            bool closedGame = false;
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.FindUnrecordedRoles = _ => UnrecordedRoles.None;
            shutdown.GameRunning = () => false;
            shutdown.CloseGame = () => closedGame = true;
            shutdown.CloseIdleSwitchers = () => Switchers();
            shutdown.ReadStream = ServiceStreamCheck.NotRunning;
            shutdown.StopSpectateRecording = () =>
                new OrphanRecordingCheck(OrphanRecordingState.None, "No recording claim.");

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(0, result.ExitCode);
            Assert.False(closedGame);
            Assert.Empty(result.Roles);
            Assert.True(result.GameClosed);
            Assert.Equal("No HeroesReplay services are running. Nothing to stop.", result.Summary);
            Assert.Equal(
                "Heroes of the Storm: not running.",
                ServiceStopResult.DescribeGameNotRunning(result.Switchers)
            );
            Assert.Null(ServiceLockStore.TryLoad(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    /// <summary>
    /// A spectate that already exited left the game open: with nothing recorded, the stop closes
    /// it and its idle switcher, and says so.
    /// </summary>
    [Fact]
    public void Stop_WithoutServicesJson_ClosesAGameNoSpectateRuns()
    {
        string path = TempLock();
        try
        {
            bool closedGame = false;
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.FindUnrecordedRoles = _ => UnrecordedRoles.None;
            shutdown.GameRunning = () => true;
            shutdown.CloseGame = () => closedGame = true;
            shutdown.CloseIdleSwitchers = () => Switchers();

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(0, result.ExitCode);
            Assert.True(closedGame);
            Assert.True(result.GameClosed);
            Assert.Equal(
                "No HeroesReplay services are running. Closed what was left running.",
                result.Summary
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_WithoutServicesJson_GameThatDoesNotClose_Fails()
    {
        string path = TempLock();
        try
        {
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.FindUnrecordedRoles = _ => UnrecordedRoles.None;
            shutdown.GameRunning = () => true;
            shutdown.CloseGame = () => false;

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Heroes of the Storm is still running.", result.Failures());
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    /// <summary>
    /// A spectate from another install is never asked, killed, or recorded, and the game and the
    /// switchers are left to it (#381).
    /// </summary>
    [Fact]
    public void Stop_LeavesAnotherInstallsSpectateAndItsGameAlone()
    {
        string path = TempLock();
        try
        {
            var processes = new FakeProcesses(93);
            bool touched = false;
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.Zero);
            shutdown.FindUnrecordedRoles = _ => new UnrecordedRoles
            {
                OtherInstalls = new[] { HandStarted(93, @"C:\heroesreplay\app\heroesreplay.exe") },
            };
            shutdown.GameRunning = () => true;
            shutdown.CloseGame = () => touched = true;
            shutdown.CloseIdleSwitchers = () =>
            {
                touched = true;
                return Switchers();
            };

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(processes.Killed);
            Assert.Empty(result.Roles);
            Assert.False(touched);
            Assert.Null(result.GameClosed);
            Assert.Null(result.Switchers);
            Assert.Equal("heroesreplay", processes.Name(93));
            Assert.Null(ServiceLockStore.TryLoad(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    /// <summary>
    /// A recorded stack and a hand-started spectate from this install: both stop, the scan gets
    /// the recorded pids so it never lists them twice, and services.json is removed.
    /// </summary>
    [Fact]
    public void Stop_RecordedRolesAndAHandStartedSpectate_StopsBothAndPassesTheRecordedPids()
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
                        new() { Name = "download", Pid = 95 },
                    },
                }
            );
            var processes = new FakeProcesses(94, 95);
            IReadOnlyCollection<int> scanned = null;
            bool closedGame = false;
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.Zero);
            shutdown.FindUnrecordedRoles = recorded =>
            {
                scanned = recorded;
                return new UnrecordedRoles { ThisInstall = new[] { HandStarted(94) } };
            };
            shutdown.CloseGame = () => closedGame = true;

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(new[] { 95 }, scanned);
            Assert.Equal(new[] { 95, 94 }, result.Roles.Select(role => role.Pid).ToArray());
            Assert.True(closedGame);
            Assert.Null(ServiceLockStore.TryLoad(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    /// <summary>
    /// #397, the stream PC on 2026-10-09: services.json still named spectate's dead pid 16380
    /// while the spectate a lost restart started, pid 21960, kept playing. The stop found only
    /// the dead pid. It now stops the untracked spectate too and closes the game it ran.
    /// </summary>
    [Fact]
    public void Stop_ServicesJsonWithADeadSpectatePid_StopsTheUntrackedSpectateThatRuns()
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
                        new() { Name = "spectate", Pid = 16380 },
                        new() { Name = "twitch", Pid = 101 },
                        new() { Name = "download", Pid = 102 },
                        new() { Name = "youtube", Pid = 103 },
                    },
                }
            );
            var processes = new FakeProcesses(101, 102, 103, 21960);
            var table = new[] { 101, 102, 103, 21960, 4242 }
                .Select(pid => new ProcessTableEntry(pid, 1, "heroesreplay.exe", Exe, null))
                .ToList();
            var lines = new Dictionary<int, string>
            {
                [101] = $"\"{Exe}\" twitch connect",
                [102] = $"\"{Exe}\" heroesprofile download",
                [103] = $"\"{Exe}\" youtube uploader",
                [21960] = $"\"{Exe}\" spectate heroesprofile",
                [4242] = $"\"{Exe}\" services stop",
            };
            IReadOnlyCollection<int> scanned = null;
            int closedGame = 0;
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.FromSeconds(20));
            shutdown.FindUnrecordedRoles = recorded =>
            {
                scanned = recorded;
                return UnrecordedRoles.Find(
                    table,
                    pid => lines.GetValueOrDefault(pid),
                    Exe,
                    selfPid: 4242,
                    recorded
                );
            };
            shutdown.RequestGracefulStop = () =>
            {
                foreach (int pid in new[] { 101, 102, 103, 21960 })
                {
                    processes.Exit(pid);
                }
            };
            shutdown.CloseGame = () =>
            {
                closedGame++;
                return true;
            };

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(new[] { 16380, 101, 102, 103 }, scanned);
            Assert.Equal(
                new[]
                {
                    "spectate pid 16380: already exited.",
                    "twitch pid 101: graceful.",
                    "download pid 102: graceful.",
                    "youtube pid 103: graceful.",
                    "spectate pid 21960: graceful. Not in services.json.",
                },
                result.Roles.Select(role => role.Describe())
            );
            Assert.Equal(1, closedGame);
            Assert.Empty(processes.Killed);
            Assert.Null(ServiceLockStore.TryLoad(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    /// <summary>
    /// #397: an untracked download, uploader, or Twitch role of this install is stopped like a
    /// role, and never written to services.json. It does not drive the game, so the game is
    /// left alone when no spectate is involved.
    /// </summary>
    [Fact]
    public void Stop_AnUntrackedNonSpectateRole_IsStoppedToo_AndLeavesTheGameAlone()
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
                        new() { Name = "download", Pid = 95 },
                    },
                }
            );
            var processes = new FakeProcesses(95, 96);
            processes.Unkillable.Add(96);
            bool closedGame = false;
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.Zero);
            shutdown.FindUnrecordedRoles = _ => new UnrecordedRoles
            {
                ThisInstall = new[]
                {
                    new ServiceProcessRecord
                    {
                        Name = "download",
                        Pid = 96,
                        Arguments = "heroesprofile download",
                        ExecutablePath = Exe,
                    },
                },
            };
            shutdown.CloseGame = () => closedGame = true;

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            // It would not die: the stop fails and says so, and services.json still does not list it.
            Assert.Equal(1, result.ExitCode);
            Assert.Equal(
                new[]
                {
                    "download pid 95: killed.",
                    "download pid 96: still running. Not in services.json. Kill failed: Access is denied.",
                },
                result.Roles.Select(role => role.Describe())
            );
            Assert.False(closedGame);
            Assert.Null(result.GameClosed);
            Assert.Null(ServiceLockStore.TryLoad(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_AScanThatThrows_StopsTheRecordedRolesAsBefore()
    {
        string path = TempLock();
        try
        {
            SaveRoles(path, 98);
            var processes = new FakeProcesses(98);
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.Zero);
            shutdown.FindUnrecordedRoles = _ =>
                throw new InvalidOperationException("snapshot failed");
            shutdown.CloseGame = () => true;

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(98, Assert.Single(result.Roles).Pid);
            Assert.True(result.GameClosed);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    private static ServiceProcessRecord HandStarted(int pid, string exe = Exe) =>
        new()
        {
            Name = UnrecordedRoles.Role,
            Pid = pid,
            Arguments = @"spectate file --path C:\heroesreplay\Replays",
            ExecutablePath = exe,
        };

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
    public void Start_NoCapture_FailsBeforeAnyChildStarts()
    {
        string path = TempLock();
        try
        {
            var messages = new List<string>();
            int starts = 0;
            ServiceStartupHandshake handshake = ServiceStartupHandshake.ReadyNow();
            handshake.Spectate = HealthySpectate();
            handshake.Spectate.CaptureOk = false;
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
            Assert.Contains(messages, message => message.Contains("capture"));
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
            ServiceStopResult result = ServiceSupervisor.Stop(
                path,
                new ServiceShutdown
                {
                    ProcessNameOrNull = pid => "heroesreplay",
                    Kill = killed.Add,
                    RequestGracefulStop = () => { },
                    GracefulWait = TimeSpan.Zero,
                    Probe = pid => new ServiceProcessProbe
                    {
                        ExecutablePath = @"C:\other\heroesreplay.exe",
                        StartedAt = started,
                    },
                }
            );

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(killed);
            Assert.Equal(ServiceStopOutcome.AlreadyExited, Assert.Single(result.Roles).Outcome);
            Assert.Null(ServiceLockStore.TryLoad(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_ReportsGracefulKilledAlreadyExitedAndStillRunningPerRole()
    {
        string path = TempLock();
        try
        {
            SaveRoles(path, 10, 11, 12, 13);
            var processes = new FakeProcesses(10, 11, 13);
            processes.Unkillable.Add(13);
            int pauses = 0;
            int streamReads = 0;
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.FromSeconds(1));
            shutdown.Wait = _ =>
            {
                pauses++;
                processes.Exit(10);
            };
            shutdown.CloseGame = () => true;
            shutdown.ReadStream = () =>
            {
                streamReads++;
                return ServiceStreamCheck.Inactive();
            };

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(
                new[]
                {
                    "spectate pid 10: graceful.",
                    "twitch pid 11: killed.",
                    "download pid 12: already exited.",
                    "youtube pid 13: still running. Kill failed: Access is denied.",
                },
                result.Roles.Select(role => role.Describe()).ToArray()
            );
            Assert.Equal(5, pauses);
            Assert.Equal(new[] { 11, 13 }, processes.Killed);
            Assert.Equal(0, streamReads);
            Assert.Null(result.Stream);
            Assert.Contains(result.Failures(), failure => failure.Contains("youtube pid 13"));
            ServiceProcessRecord kept = Assert.Single(ServiceLockStore.TryLoad(path).Processes);
            Assert.Equal(13, kept.Pid);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_KilledRoles_SucceedOnlyAfterTheGameClosedAndObsIsNotStreaming()
    {
        string path = TempLock();
        try
        {
            SaveRoles(path, 20, 21);
            var processes = new FakeProcesses(20, 21);
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.Zero);
            shutdown.CloseGame = () => true;
            shutdown.ReadStream = ServiceStreamCheck.Inactive;

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(0, result.ExitCode);
            Assert.All(result.Roles, role => Assert.Equal(ServiceStopOutcome.Killed, role.Outcome));
            Assert.True(result.GameClosed);
            Assert.Equal(ServiceStreamState.Inactive, result.Stream.State);
            Assert.Empty(result.Failures());
            Assert.Null(ServiceLockStore.TryLoad(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_FailsWhenTheGameIsStillRunning()
    {
        string path = TempLock();
        try
        {
            SaveRoles(path, 30);
            ServiceShutdown shutdown = new FakeProcesses(30).Shutdown(TimeSpan.Zero);
            shutdown.CloseGame = () => false;
            shutdown.ReadStream = ServiceStreamCheck.NotRunning;

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(1, result.ExitCode);
            Assert.False(result.GameClosed);
            Assert.Contains(result.Failures(), failure => failure.Contains("Heroes of the Storm"));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_FailsWhenObsIsStillStreamingAfterTheRolesExited()
    {
        string path = TempLock();
        try
        {
            SaveRoles(path, 40, 41);
            var processes = new FakeProcesses(40, 41);
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.FromSeconds(5));
            shutdown.RequestGracefulStop = () =>
            {
                processes.Exit(40);
                processes.Exit(41);
            };
            shutdown.CloseGame = () => true;
            shutdown.ReadStream = ServiceStreamCheck.Active;

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(1, result.ExitCode);
            Assert.All(
                result.Roles,
                role => Assert.Equal(ServiceStopOutcome.Graceful, role.Outcome)
            );
            Assert.Empty(processes.Killed);
            Assert.Contains(result.Failures(), failure => failure.Contains("still streaming"));
            Assert.Null(ServiceLockStore.TryLoad(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Theory]
    [InlineData(ServiceStreamState.NotRunning, 0)]
    [InlineData(ServiceStreamState.Unreachable, 1)]
    [InlineData(ServiceStreamState.Inactive, 0)]
    [InlineData(ServiceStreamState.Active, 1)]
    [InlineData(ServiceStreamState.Unknown, 1)]
    public void Stop_OnlyAClosedObsOrAnInactiveStreamConfirmsTheStop(
        ServiceStreamState state,
        int expected
    )
    {
        string path = TempLock();
        try
        {
            SaveRoles(path, 60);
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.CloseGame = () => true;
            shutdown.ReadStream = () => new ServiceStreamCheck(state, "fake");

            Assert.Equal(expected, ServiceSupervisor.Stop(path, shutdown).ExitCode);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_UnreachableObs_HoldsTheStopOnlyWhereThisInstallStreams()
    {
        ServiceStreamCheck streams = ServiceStreamCheck.WhenUnreachable(true, "no answer.");
        ServiceStreamCheck doesNot = ServiceStreamCheck.WhenUnreachable(false, "no answer.");

        Assert.False(streams.ConfirmsStopped);
        Assert.Equal(ServiceStreamState.Unreachable, streams.State);
        Assert.True(doesNot.ConfirmsStopped);
        Assert.Equal(ServiceStreamState.NotStreamedHere, doesNot.State);
        Assert.Contains("OBS:StreamingEnabled is false", doesNot.Detail);
    }

    [Fact]
    public void Stop_RunningObsWhoseWebsocketDoesNotAnswer_SaysTheStreamMayBeLive()
    {
        string path = TempLock();
        try
        {
            SaveRoles(path, 60);
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.CloseGame = () => true;
            shutdown.ReadStream = () =>
                ServiceStreamCheck.Unreachable(
                    "OBS websocket at ws://127.0.0.1:4455 did not identify within 5s."
                );

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.False(result.Succeeded);
            Assert.Contains(
                result.Failures(),
                failure =>
                    failure.Contains("may still be live", StringComparison.Ordinal)
                    || failure.Contains("not confirmed stopped", StringComparison.Ordinal)
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_ObsReaderThatThrows_IsNotConfirmed()
    {
        string path = TempLock();
        try
        {
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.ReadStream = () => throw new InvalidOperationException("socket broke");

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(ServiceStreamState.Unknown, result.Stream.State);
            Assert.Contains("socket broke", result.Stream.Detail);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    /// <summary>
    /// #318: once every role exited, the stream is read as before, then the recording spectate
    /// left running is stopped. The stop succeeds.
    /// </summary>
    [Fact]
    public void Stop_StopsTheRecordingSpectateLeftAfterTheStreamCheck()
    {
        string path = TempLock();
        try
        {
            SaveRoles(path, 80, 81);
            var processes = new FakeProcesses(80, 81);
            var steps = new List<string>();
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.Zero);
            shutdown.CloseGame = () =>
            {
                steps.Add("game");
                return true;
            };
            shutdown.ReadStream = () =>
            {
                steps.Add("stream");
                return ServiceStreamCheck.Inactive();
            };
            shutdown.StopSpectateRecording = () =>
            {
                steps.Add("recording");
                return new OrphanRecordingCheck(
                    OrphanRecordingState.Stopped,
                    "Stopped the recording spectate pid 80 started for replay 65820711.",
                    "2026-10-08 12-33-26.mp4"
                );
            };

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(new[] { "game", "stream", "recording" }, steps);
            Assert.Equal(OrphanRecordingState.Stopped, result.Recording.State);
            Assert.Equal(ServiceStreamState.Inactive, result.Stream.State);
            Assert.Empty(result.Failures());
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Theory]
    [InlineData(OrphanRecordingState.None, 0)]
    [InlineData(OrphanRecordingState.ClaimantRunning, 0)]
    [InlineData(OrphanRecordingState.ObsNotRunning, 0)]
    [InlineData(OrphanRecordingState.Inactive, 0)]
    [InlineData(OrphanRecordingState.Stopped, 0)]
    [InlineData(OrphanRecordingState.NotOwned, 0)]
    [InlineData(OrphanRecordingState.Unreachable, 1)]
    [InlineData(OrphanRecordingState.Failed, 1)]
    [InlineData(OrphanRecordingState.Unknown, 1)]
    public void Stop_FailsOnlyWhileARecordingSpectateStartedMayStillRun(
        OrphanRecordingState state,
        int expected
    )
    {
        string path = TempLock();
        try
        {
            SaveRoles(path, 82);
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.CloseGame = () => true;
            shutdown.ReadStream = ServiceStreamCheck.NotRunning;
            shutdown.StopSpectateRecording = () => new OrphanRecordingCheck(state, "fake");

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(expected, result.ExitCode);
            Assert.Equal(
                expected == 1,
                result.Failures().Any(failure => failure.Contains("OBS recording spectate"))
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_RoleStillRunning_DoesNotTouchTheRecording()
    {
        string path = TempLock();
        try
        {
            SaveRoles(path, 83);
            var processes = new FakeProcesses(83);
            processes.Unkillable.Add(83);
            int recordingChecks = 0;
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.Zero);
            shutdown.CloseGame = () => true;
            shutdown.ReadStream = ServiceStreamCheck.Inactive;
            shutdown.StopSpectateRecording = () =>
            {
                recordingChecks++;
                return new OrphanRecordingCheck(OrphanRecordingState.None, null);
            };

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(0, recordingChecks);
            Assert.Null(result.Recording);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_RecordingStepThatThrows_IsNotConfirmed()
    {
        string path = TempLock();
        try
        {
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.ReadStream = ServiceStreamCheck.NotRunning;
            shutdown.StopSpectateRecording = () =>
                throw new InvalidOperationException("claim locked");

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(OrphanRecordingState.Unknown, result.Recording.State);
            Assert.Contains("claim locked", result.Recording.Detail);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_KeepsTheTwentySecondGracefulBudgetBeforeKilling()
    {
        string path = TempLock();
        try
        {
            SaveRoles(path, 70);
            var processes = new FakeProcesses(70);
            TimeSpan waited = TimeSpan.Zero;
            var shutdown = new ServiceShutdown
            {
                ProcessNameOrNull = processes.Name,
                Kill = pid =>
                {
                    Assert.Equal(TimeSpan.FromSeconds(20), waited);
                    processes.Kill(pid);
                },
                RequestGracefulStop = () => { },
                Wait = pause => waited += pause,
            };

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(TimeSpan.FromSeconds(20), ServiceShutdown.DefaultGracefulWait);
            Assert.Equal(TimeSpan.FromSeconds(20), waited);
            Assert.Equal(ServiceStopOutcome.Killed, Assert.Single(result.Roles).Outcome);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Start_WithRoles_StartsOnlyThoseInPlanOrder()
    {
        string path = TempLock();
        try
        {
            var args = new List<string>();
            int code = ServiceSupervisor.Start(
                path,
                Exe,
                pid => null,
                (name, arguments) =>
                {
                    args.Add(arguments);
                    return 300 + args.Count;
                },
                handshake: ServiceStartupHandshake.ReadyNow(),
                roles: new[] { "youtube", "download" }
            );

            Assert.Equal(0, code);
            Assert.Equal(new[] { "heroesprofile download", "youtube uploader" }, args);
            Assert.Equal(
                new[] { "download", "youtube" },
                ServiceLockStore.TryLoad(path).Processes.Select(record => record.Name)
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Theory]
    [InlineData(null, "spectate,twitch,download,youtube")]
    [InlineData("", "spectate,twitch,download,youtube")]
    [InlineData("youtube,download", "download,youtube")]
    [InlineData(" Download ; youtube ", "download,youtube")]
    public void ParseRoles_KeepsPlanOrder(string value, string expected)
    {
        IReadOnlyList<string> roles = ServiceProcessPlan.ParseRoles(value, out string error);
        Assert.Null(error);
        Assert.Equal(expected, string.Join(",", roles));
    }

    [Fact]
    public void ParseRoles_RejectsAnUnknownRole()
    {
        Assert.Null(ServiceProcessPlan.ParseRoles("download,obs", out string error));
        Assert.Contains("Unknown role obs", error);
        Assert.Contains("spectate, twitch, download, youtube", error);
    }

    [Fact]
    public void Stop_WaitsForTheSupervisor_ThenStopsTheRoleItWasRestarting()
    {
        string path = TempLock();
        try
        {
            SaveRoles(path, 40);
            var processes = new FakeProcesses(40);
            ServiceShutdown shutdown = processes.Shutdown(TimeSpan.Zero);
            var order = new List<string>();
            shutdown.RequestGracefulStop = () => order.Add("stop file");
            shutdown.StopSupervisor = () =>
            {
                order.Add("supervisor");
                // It recorded a restarted youtube before it saw the stop file.
                ServiceLock current = ServiceLockStore.TryLoad(path);
                current.Processes.Add(
                    new ServiceProcessRecord
                    {
                        Name = "youtube",
                        Pid = 41,
                        Nonce = "restarted",
                    }
                );
                ServiceLockStore.Save(path, current);
                processes.Start(41);
                return new ServiceRoleStop("supervisor", 9, ServiceStopOutcome.Graceful);
            };
            bool cleared = false;
            shutdown.ClearStopFile = () => cleared = true;

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(new[] { "stop file", "supervisor" }, order);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(ServiceStopOutcome.Graceful, result.Supervisor.Outcome);
            Assert.Equal(new[] { 40, 41 }, processes.Killed);
            Assert.Equal(new[] { "spectate", "youtube" }, result.Roles.Select(role => role.Name));
            Assert.Null(ServiceLockStore.TryLoad(path));
            Assert.True(cleared);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Stop_FailsAndKeepsTheStopFileWhileTheSupervisorIsStillRunning()
    {
        string path = TempLock();
        try
        {
            ServiceShutdown shutdown = new FakeProcesses().Shutdown(TimeSpan.Zero);
            shutdown.StopSupervisor = () =>
                new ServiceRoleStop(
                    "supervisor",
                    9,
                    ServiceStopOutcome.StillRunning,
                    "Kill failed: Access is denied."
                );
            bool cleared = false;
            shutdown.ClearStopFile = () => cleared = true;

            ServiceStopResult result = ServiceSupervisor.Stop(path, shutdown);

            Assert.Equal(1, result.ExitCode);
            Assert.False(cleared);
            Assert.Contains(result.Failures(), failure => failure.Contains("supervisor pid 9"));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Status_ShowsEachRolesLogPathAndTheSupervisor()
    {
        string path = TempLock();
        string logs = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-logs-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, "download-2026-10-01.log"), "");
            var output = new StringWriter();
            int code = ServiceSupervisor.Status(
                path,
                pid => null,
                spectator: null,
                query: new ServiceStatusQuery
                {
                    Output = CliOutputFormat.Json,
                    Out = output,
                    Time = new FixedClock(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero)),
                    LogDirectory = logs,
                    SupervisorLiveness = () => ServiceSupervisorLiveness.ByMutex,
                    ReadSupervisor = () =>
                        new ServiceSupervisorState
                        {
                            Pid = 77,
                            Budget = 5,
                            BudgetWindowSeconds = 1800,
                            BackoffSeconds = new List<long> { 10, 30, 120, 300 },
                            Supervised = new List<string> { "download" },
                        },
                }
            );

            Assert.Equal(0, code);
            using var json = System.Text.Json.JsonDocument.Parse(output.ToString());
            var roles = json.RootElement.GetProperty("roles").EnumerateArray().ToList();
            Assert.Equal(
                Path.Combine(logs, "download-2026-10-01.log"),
                roles
                    .Single(role => role.GetProperty("role").GetString() == "download")
                    .GetProperty("logPath")
                    .GetString()
            );
            Assert.Equal(
                Path.Combine(logs, "twitch-2026-10-02.log"),
                roles
                    .Single(role => role.GetProperty("role").GetString() == "twitch")
                    .GetProperty("logPath")
                    .GetString()
            );
            System.Text.Json.JsonElement supervisor = json.RootElement.GetProperty("supervisor");
            Assert.True(supervisor.GetProperty("running").GetBoolean());
            Assert.Equal(77, supervisor.GetProperty("pid").GetInt32());

            var text = new StringWriter();
            ServiceSupervisor.Status(
                path,
                pid => null,
                spectator: null,
                query: new ServiceStatusQuery { Out = text, LogDirectory = logs }
            );
            Assert.Contains("Supervisor: not running.", text.ToString());
            Assert.Contains(
                "Log: " + Path.Combine(logs, "download-2026-10-01.log"),
                text.ToString()
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
            Directory.Delete(logs, recursive: true);
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedClock(DateTimeOffset now)
        {
            this.now = now;
        }

        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static void SaveRoles(string path, params int[] pids)
    {
        string[] names = { "spectate", "twitch", "download", "youtube" };
        var records = new List<ServiceProcessRecord>();
        for (int index = 0; index < pids.Length; index++)
        {
            records.Add(new ServiceProcessRecord { Name = names[index], Pid = pids[index] });
        }

        ServiceLockStore.Save(
            path,
            new ServiceLock { StartedAt = DateTimeOffset.UtcNow, Processes = records }
        );
    }

    /// <summary>Pids that are alive answer as heroesreplay. A kill ends them unless unkillable.</summary>
    private sealed class FakeProcesses
    {
        private readonly HashSet<int> alive;

        public FakeProcesses(params int[] pids)
        {
            alive = new HashSet<int>(pids);
        }

        public HashSet<int> Unkillable { get; } = new();

        public List<int> Killed { get; } = new();

        public string Name(int pid) => alive.Contains(pid) ? "heroesreplay" : null;

        public void Exit(int pid) => alive.Remove(pid);

        public void Start(int pid) => alive.Add(pid);

        public void Kill(int pid)
        {
            Killed.Add(pid);
            if (Unkillable.Contains(pid))
            {
                throw new InvalidOperationException("Access is denied.");
            }

            alive.Remove(pid);
        }

        public ServiceShutdown Shutdown(TimeSpan gracefulWait) =>
            new()
            {
                ProcessNameOrNull = Name,
                Kill = Kill,
                RequestGracefulStop = () => { },
                GracefulWait = gracefulWait,
                Wait = _ => { },
                ClearStopFile = () => { },
                // Never the real process table: a fake pid can be a real process here.
                Probe = _ => null,
            };
    }

    private const string Exe = @"C:\heroesreplay\heroesreplay.exe";

    private static SpectateStartupFacts HealthySpectate()
    {
        return new SpectateStartupFacts
        {
            LaunchPath = Exe,
            CaptureOk = true,
            PathsOk = true,
            ObsOk = true,
        };
    }

    [Fact]
    public void Status_ReportsTheMachineWithoutChangingTheExitCode()
    {
        string path = TempLock();
        try
        {
            MachineHealthReport machine = MachineHealth.Evaluate(
                new MachineHealthSnapshot
                {
                    PhysicalTotalBytes = 16L << 30,
                    PhysicalAvailableBytes = 4L << 30,
                    CommitBytes = 23L << 30,
                    CommitLimitBytes = 25L << 30,
                    AgentProcesses = 83,
                    ConhostProcesses = 85,
                    HeroesProcesses = 1,
                },
                new MachineHealthSettings()
            );
            var text = new StringWriter();
            int code = ServiceSupervisor.Status(
                path,
                pid => null,
                spectator: null,
                query: new ServiceStatusQuery { Out = text, ReadMachine = () => machine }
            );

            Assert.Equal(0, code);
            string output = text.ToString();
            Assert.Contains("Machine: memory 75% (12288 of 16384 MB), commit 92%", output);
            Assert.Contains("Agent.exe 83, conhost.exe 85", output);
            Assert.Contains("WARN 83 Battle.net Agent.exe processes are running", output);

            var json = new StringWriter();
            ServiceSupervisor.Status(
                path,
                pid => null,
                spectator: null,
                query: new ServiceStatusQuery
                {
                    Output = CliOutputFormat.Json,
                    Out = json,
                    ReadMachine = () => throw new InvalidOperationException("denied"),
                }
            );
            using var document = System.Text.Json.JsonDocument.Parse(json.ToString());
            System.Text.Json.JsonElement read = document.RootElement.GetProperty("machine");
            Assert.False(read.GetProperty("ok").GetBoolean());
            Assert.Contains("denied", read.GetProperty("warnings")[0].GetString());
            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Status_ShowsARollbackThatWaitsForOBSWithoutChangingTheExitCode()
    {
        string path = TempLock();
        try
        {
            const string Waiting =
                "A release rollback waits to put back x.bak over HeroesReplay.json.";
            var text = new StringWriter();
            int code = ServiceSupervisor.Status(
                path,
                pid => null,
                spectator: null,
                query: new ServiceStatusQuery { Out = text, ReadObsRestorePending = () => Waiting }
            );

            Assert.Equal(0, code);
            Assert.Contains("OBS rollback: waiting. " + Waiting, text.ToString());

            var json = new StringWriter();
            ServiceSupervisor.Status(
                path,
                pid => null,
                spectator: null,
                query: new ServiceStatusQuery
                {
                    Output = CliOutputFormat.Json,
                    Out = json,
                    ReadObsRestorePending = () => Waiting,
                }
            );
            using var document = System.Text.Json.JsonDocument.Parse(json.ToString());
            Assert.Equal(
                Waiting,
                document.RootElement.GetProperty("obsRestorePending").GetString()
            );

            var none = new StringWriter();
            ServiceSupervisor.Status(
                path,
                pid => null,
                spectator: null,
                query: new ServiceStatusQuery { Out = none, ReadObsRestorePending = () => null }
            );
            Assert.DoesNotContain("OBS rollback", none.ToString());
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    private static string TempLock() =>
        Path.Combine(Path.GetTempPath(), $"heroesreplay-services-{Guid.NewGuid():N}.json");
}
