using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.ServiceHost.Logs;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.Services;

public class ServicesCommand : Command
{
    public ServicesCommand()
        : base(
            "services",
            "Start, stop, or inspect the spectator, Twitch, downloader, and YouTube processes."
        )
    {
        Subcommands.Add(StartCommand());
        Subcommands.Add(EnsureCommand());
        Subcommands.Add(StopCommand());
        Subcommands.Add(StatusCommand());
        Subcommands.Add(SuperviseCommand());
        Subcommands.Add(InstallTaskCommand());
    }

    private static Command InstallTaskCommand()
    {
        var command = new Command(
            "install-task",
            "Register the Windows scheduled task HeroesReplay-live: `services start --supervise` from this install when you log on, interactive and not elevated (no administrator rights needed). apply-release.ps1 restarts the stack through it after an update, so the stack comes back supervised. --remove deletes it."
        );
        var environment = new Option<string>("--environment")
        {
            Description =
                "HEROES_REPLAY_ENV the task sets (prod on the stream PC). Default: this shell's HEROES_REPLAY_ENV; with neither, the task inherits the user's environment.",
        };
        var roles = new Option<string>("--roles")
        {
            Description =
                "Roles the task starts, as for `services start --roles`. Default: all four.",
        };
        var name = new Option<string>("--name")
        {
            Description = "Task name. apply-release.ps1 restarts through HeroesReplay-live.",
            DefaultValueFactory = _ => ServiceLogonTask.DefaultName,
        };
        var remove = new Option<bool>("--remove") { Description = "Delete the task instead." };
        command.Options.Add(environment);
        command.Options.Add(roles);
        command.Options.Add(name);
        command.Options.Add(remove);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string task = parseResult.GetValue(name);
                if (parseResult.GetValue(remove))
                {
                    return Task.FromResult(Schtasks("/Delete", "/TN", task, "/F"));
                }

                string selected = null;
                if (!string.IsNullOrWhiteSpace(parseResult.GetValue(roles)))
                {
                    IReadOnlyList<string> parsed = ServiceProcessPlan.ParseRoles(
                        parseResult.GetValue(roles),
                        out string error
                    );
                    if (parsed == null)
                    {
                        Console.Error.WriteLine(error);
                        return Task.FromResult(1);
                    }

                    selected = string.Join(",", parsed);
                }

                return Task.FromResult(
                    InstallTask(
                        task,
                        parseResult.GetValue(environment)
                            ?? Environment.GetEnvironmentVariable("HEROES_REPLAY_ENV"),
                        selected
                    )
                );
            }
        );
        return command;
    }

    private static int InstallTask(string task, string environment, string roles)
    {
        string exe = Environment.ProcessPath;
        if (ReleaseInstall.LooksLikeSourceBuild(Path.GetDirectoryName(exe)))
        {
            Console.WriteLine(
                $"Warning: {exe} is a source build. The task starts this file at every logon; run install-task from the release install (C:\\heroesreplay\\app) on the stream PC."
            );
        }

        string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        string file = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-task-" + Guid.NewGuid() + ".xml"
        );
        try
        {
            File.WriteAllText(
                file,
                ServiceLogonTask.Xml(user, exe, environment, roles),
                Encoding.Unicode
            );
            int code = Schtasks("/Create", "/TN", task, "/XML", file, "/F");
            if (code == 0)
            {
                Console.WriteLine(
                    $"Task {task} runs `{ServiceLogonTask.Arguments(roles)}` from {Path.GetDirectoryName(exe)} 30 s after {user} logs on"
                        + (
                            string.IsNullOrWhiteSpace(environment)
                                ? "."
                                : $", with HEROES_REPLAY_ENV={environment}."
                        )
                        + " apply-release.ps1 restarts the stack through it. Start it now with `schtasks /Run /TN "
                        + task
                        + "`; remove it with `heroesreplay services install-task --remove`."
                );
            }

            return code;
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static int Schtasks(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start);
        process.WaitForExit();
        return process.ExitCode == 0 ? 0 : 1;
    }

    private static Command StartCommand()
    {
        var command = new Command(
            "start",
            "Start spectate, twitch connect, heroesprofile download, and youtube uploader. Does not start Twitch ingest."
        );
        var supervise = new Option<bool>("--supervise")
        {
            Description =
                "Stay in the foreground as the supervisor after the roles are ready: restart failed roles with backoff (10s, 30s, 2m, 5m), kill and restart stale ones, at most 5 restarts per role in 30 minutes (ServiceRestart). `services stop` ends it.",
        };
        var roles = new Option<string>("--roles")
        {
            Description =
                "Comma-separated subset to start, in plan order: spectate, twitch, download, youtube. Default: all four. For proofs, e.g. `--roles download,youtube`.",
        };
        command.Options.Add(supervise);
        command.Options.Add(roles);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                IReadOnlyList<string> selected = ServiceProcessPlan.ParseRoles(
                    parseResult.GetValue(roles),
                    out string error
                );
                if (selected == null)
                {
                    Console.Error.WriteLine(error);
                    return Task.FromResult(1);
                }

                bool supervised = parseResult.GetValue(supervise);
                // Refuses while a supervisor runs in this session or another (#293).
                if (!SupervisorGate().TryStart(supervised, out ServiceSupervisorMutex claim))
                {
                    return Task.FromResult(1);
                }

                using (claim)
                {
                    string exe = Environment.ProcessPath;
                    int code;
                    using (var startup = new ConsoleCapture())
                    {
                        code = StartRoles(exe, selected);
                        if (code != 0)
                        {
                            ServiceStartFailureLog.Write(
                                ServiceCollectionExtensions.LoadServiceLogSettings(),
                                code,
                                startup.Text
                            );
                        }
                    }

                    if (code != 0 || !supervised)
                    {
                        return Task.FromResult(code);
                    }

                    return Task.FromResult(Supervise(exe, cancellationToken));
                }
            }
        );
        return command;
    }

    private static int StartRoles(string exe, IReadOnlyList<string> selected)
    {
        PatchObsCollection(exe);
        ServiceStartupHandshake handshake = CreateHandshake(exe);
        if (handshake == null)
        {
            return 1;
        }

        return ServiceSupervisor.Start(
            ServiceLockStore.DefaultPath,
            exe,
            ProcessNameOrNull,
            (name, arguments) => StartProcess(exe, arguments, handshake.Pending),
            () => ServiceStopFile.Clear(),
            () =>
            {
                AspireDashboardHost.EnsureRunning();
            },
            handshake,
            selected
        );
    }

    private static Command SuperviseCommand()
    {
        var command = new Command(
            "supervise",
            "Supervise the roles `services start` recorded, in the foreground: restart failed roles with backoff (10s, 30s, 2m, 5m), kill and restart roles whose heartbeat is 2 minutes old, at most 5 restarts per role in 30 minutes (ServiceRestart), then leave the role down (service.restart_budget_exhausted). One supervisor at a time, across logon sessions (an SSH session sees the desktop's through supervisor.json). `services stop` ends it; Ctrl+C leaves the roles running unsupervised."
        );
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                // Refuses while a supervisor runs in this session or another (#293).
                using ServiceSupervisorMutex claim = SupervisorGate().TryClaim();
                if (claim == null)
                {
                    return Task.FromResult(1);
                }

                if (File.Exists(ServiceStopFile.DefaultPath))
                {
                    Console.Error.WriteLine(
                        "A stop is in progress (services.stop). Run `heroesreplay services start --supervise` once it finishes."
                    );
                    return Task.FromResult(1);
                }

                return Task.FromResult(Supervise(Environment.ProcessPath, cancellationToken));
            }
        );
        return command;
    }

    /// <summary>
    /// Runs the supervisor in this process until <c>services stop</c> or Ctrl+C. The caller holds
    /// the supervisor mutex on this thread.
    /// </summary>
    private static int Supervise(string exe, CancellationToken cancellationToken)
    {
        ServiceConsoleTitle.Apply(ServiceRoleLog.SupervisorRole);
        ServiceRestartSettings settings = ServiceCollectionExtensions.LoadServiceRestartSettings();
        using ServiceProvider provider = new ServiceCollection()
            .AddSupervisorServices()
            .BuildHeroesReplayProvider();
        ServiceRoleLogProvider log = provider
            .GetServices<ILoggerProvider>()
            .OfType<ServiceRoleLogProvider>()
            .FirstOrDefault();
        var supervision = new ServiceSupervision
        {
            Settings = settings,
            Health = ServiceCollectionExtensions.LoadServiceHealthSettings(),
            ProcessNameOrNull = ProcessNameOrNull,
            Probe = ServiceProcessProbe.TryFromProcess,
            ReadHeartbeat = record => ServiceReadyFile.TryRead(record),
            DeleteHeartbeat = record => ServiceReadyFile.Delete(record?.Nonce),
            StopRequested = () => File.Exists(ServiceStopFile.DefaultPath),
            Launch = request =>
                LaunchRole(
                    exe,
                    request.Role,
                    request.Started,
                    request.ReadyTimeout,
                    request.Waiting
                ),
            Kill = Kill,
            FindUntracked = (role, tracked) =>
                UntrackedRoleProcesses.Find(
                    role,
                    ProcessTable.Snapshot(),
                    ProcessCommandLine.TryRead,
                    exe,
                    Environment.ProcessId,
                    tracked,
                    (pid, name) => ServiceReadyFile.FindByPid(pid, name)
                ),
            CommitPercent = ReadCommitPercent,
            CloseGame = StopSpectatedGame,
            SpectateDown = () => MakeObsSafe(settings.SpectateDownObs),
            Wait = pause => cancellationToken.WaitHandle.WaitOne(pause),
            Logger = provider.GetRequiredService<ILogger<ServiceSupervision>>(),
            ExecutablePath = exe,
            Version = ServiceReadyFile.CurrentVersion(),
            LogPath = () => log?.CurrentPath,
            MachineHealth = new MachineHealthLog(
                ServiceCollectionExtensions.LoadMachineHealthSettings(),
                provider.GetRequiredService<ILogger<MachineHealthLog>>()
            ),
        };
        return supervision.Run(cancellationToken);
    }

    /// <summary>
    /// One role through the <c>services start</c> launch: its prerequisites, its arguments, a new
    /// nonce, and the ready handshake. A stop request ends the ready wait. The supervisor's
    /// restarts and <c>services ensure</c> use it.
    /// </summary>
    private static ServiceLaunch LaunchRole(
        string exe,
        string role,
        Action<ServiceProcessRecord> started,
        TimeSpan? readyTimeout = null,
        Action waiting = null
    )
    {
        ServiceStartupHandshake handshake = CreateHandshake(exe);
        if (handshake == null)
        {
            return new ServiceLaunch(null, false, false, "role configuration could not be loaded.");
        }

        Func<bool> stopping = () => File.Exists(ServiceStopFile.DefaultPath);
        handshake.Cancelled = stopping;
        if (readyTimeout is TimeSpan wait && wait > TimeSpan.Zero)
        {
            handshake.ReadyTimeout = wait;
        }

        if (waiting != null)
        {
            handshake.Wait = pause =>
            {
                Thread.Sleep(pause);
                waiting();
            };
        }

        TimeSpan launcherWait = LauncherWait(handshake.ReadyTimeout);
        return ServiceSupervisor.Restart(
            role,
            exe,
            (name, arguments) =>
                StartProcess(exe, arguments, handshake.Pending, launcherWait, stopping),
            ProcessNameOrNull,
            handshake,
            started
        );
    }

    /// <summary>
    /// How long a launch waits for the PowerShell launcher to report the pid: 15 s, longer with
    /// a longer ready wait (a third of it, at most 60 s), because PowerShell itself starts slowly
    /// on a machine short of memory (#397).
    /// </summary>
    private static TimeSpan LauncherWait(TimeSpan readyTimeout)
    {
        TimeSpan third = readyTimeout / 3;
        if (third < DefaultLauncherWait)
        {
            return DefaultLauncherWait;
        }

        return third > MaxLauncherWait ? MaxLauncherWait : third;
    }

    private static readonly TimeSpan DefaultLauncherWait = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxLauncherWait = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The role process a launch started when the launcher did not report its pid (#397). Its
    /// ready file names its pid once it is ready; before that, it is this install's heroesreplay
    /// with the role's command line, started since the launch began, that services.json does not
    /// track. Looks for about three seconds.
    /// </summary>
    private static int? FindStartedRole(
        string exe,
        ServiceProcessRecord pending,
        DateTimeOffset began
    )
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (
                ServiceReadyFile.TryRead(pending)?.Pid is int ready
                && ready > 0
                && ServiceProcessPlan.IsHeroesReplay(ProcessNameOrNull(ready))
            )
            {
                return ready;
            }

            var tracked = new HashSet<int> { Environment.ProcessId };
            foreach (
                ServiceProcessRecord record in ServiceLockStore
                    .TryLoad(ServiceLockStore.DefaultPath)
                    ?.Processes
                    ?? new List<ServiceProcessRecord>()
            )
            {
                if (record?.Pid > 0)
                {
                    tracked.Add(record.Pid);
                }
            }

            UntrackedRoleProcess newest = UntrackedRoleProcesses
                .Find(
                    pending.Name,
                    ProcessTable.Snapshot(),
                    ProcessCommandLine.TryRead,
                    exe,
                    Environment.ProcessId,
                    tracked
                )
                .Where(item =>
                    item.Process.StartedAt is DateTimeOffset started
                    && started >= began - TimeSpan.FromSeconds(2)
                )
                .OrderByDescending(item => item.Process.StartedAt)
                .FirstOrDefault();
            if (newest != null)
            {
                return newest.Process.Pid;
            }

            Thread.Sleep(500);
        }

        return null;
    }

    /// <summary>The commit charge, percent of the commit limit, or null when it cannot be read.</summary>
    private static double? ReadCommitPercent()
    {
        MachineHealthSettings settings = ServiceCollectionExtensions.LoadMachineHealthSettings();
        MachineHealthReport report = MachineHealth.Evaluate(
            MachineHealthProbe.Read(settings),
            settings
        );
        return report.CommitLimitMegabytes > 0 ? report.CommitPercent : null;
    }

    private static Command EnsureCommand()
    {
        var command = new Command(
            "ensure",
            "Make sure the requested roles run from this install: start only the ones that are down (failed, exited, or never started), through the same startup checks as `services start`, and leave running ones alone. Never stops a running role and never mixes builds. With --supervise, attaches a supervisor when none runs. Exit 0: service.ensure_noop (nothing to do) or service.ensure_started. Exit 1, starting nothing: service.ensure_mismatch (a role runs from another install path or version), service.ensure_stop_pending (services.stop is down), service.ensure_budget_exhausted, service.ensure_stale (a requested role is alive but stale), service.ensure_supervisor_running (a requested role is down while a supervisor runs in any session; the supervisor owns its restarts), service.ensure_busy (another ensure runs). service.ensure_start_failed stops again what this ensure started."
        );
        var supervise = new Option<bool>("--supervise")
        {
            Description =
                "Keep this console as the supervisor afterwards when none runs in any session (as `services start --supervise`). With a supervisor already running, ensure only checks.",
        };
        var roles = new Option<string>("--roles")
        {
            Description =
                "Comma-separated roles to ensure: spectate, twitch, download, youtube. Default: all four. For proofs, e.g. `--roles download,youtube`.",
        };
        Option<string> output = CliOutput.CreateOption(
            "JSON: the services status envelope (schemaVersion, ok, code, message, remediation, roles[] with action running/start/started/start_failed/blocked)."
        );
        command.Options.Add(supervise);
        command.Options.Add(roles);
        command.Options.Add(output);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                IReadOnlyList<string> selected = ServiceProcessPlan.ParseRoles(
                    parseResult.GetValue(roles),
                    out string error
                );
                if (selected == null)
                {
                    Console.Error.WriteLine(error);
                    return Task.FromResult(1);
                }

                bool json = CliOutput.Format(parseResult, output) == CliOutputFormat.Json;
                return Task.FromResult(
                    Ensure(selected, parseResult.GetValue(supervise), json, cancellationToken)
                );
            }
        );
        return command;
    }

    private static int Ensure(
        IReadOnlyList<string> selected,
        bool supervise,
        bool json,
        CancellationToken cancellationToken
    )
    {
        string exe = Environment.ProcessPath;
        TextWriter stdout = Console.Out;
        ServiceEnsureLock busy = ServiceEnsureLock.TryAcquire();
        if (busy == null)
        {
            Write(
                new ServiceEnsureReport
                {
                    Ok = false,
                    Code = ServiceEnsureCodes.Busy,
                    Message = "Another `heroesreplay services ensure` is running.",
                    Remediation = "Wait for it to finish, then run ensure again.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    Supervise = supervise,
                    Requested = selected,
                },
                json,
                stdout
            );
            return 1;
        }

        ServiceSupervisorMutex claim = null;
        try
        {
            ServiceHealthSettings health = ServiceCollectionExtensions.LoadServiceHealthSettings();
            var ensure = new ServiceEnsure
            {
                ExecutablePath = exe,
                Version = ServiceSupervisor.CurrentVersion(),
                Roles = selected,
                Supervise = supervise,
                Environment = Environment.GetEnvironmentVariable("HEROES_REPLAY_ENV"),
                Health = health,
                ProcessNameOrNull = ProcessNameOrNull,
                Probe = ServiceProcessProbe.TryFromProcess,
                ReadHeartbeat = record => ServiceReadyFile.TryRead(record),
                StopRequested = () => File.Exists(ServiceStopFile.DefaultPath),
                ReadSupervisor = () =>
                    ServiceSupervisorFile.TryLoad(ServiceSupervisorFile.DefaultPath),
                SupervisorLiveness = () =>
                    ServiceSupervisorFile.Check(freshFor: ServiceSupervisorFile.FreshFor(health)),
                Launch = (role, started) => LaunchRole(exe, role, started),
                BeforeStart = starting =>
                {
                    if (starting.Contains("spectate", StringComparer.OrdinalIgnoreCase))
                    {
                        PatchObsCollection(exe);
                    }

                    try
                    {
                        AspireDashboardHost.EnsureRunning();
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine(
                            $"Aspire dashboard was not started. Continuing without the dashboard. {e.Message}"
                        );
                    }
                },
                Kill = Kill,
                DeleteHeartbeat = record => ServiceReadyFile.Delete(record?.Nonce),
                Log = json ? Console.Error : stdout,
            };

            ServiceEnsureReport plan = ensure.Plan();
            if (plan.Ok && plan.SupervisorAttached)
            {
                // Claimed before any start, as `services start --supervise` does (#293).
                claim = SupervisorGate().TryClaim();
                if (claim == null)
                {
                    plan = plan with
                    {
                        Ok = false,
                        Code = ServiceEnsureCodes.SupervisorRunning,
                        Message =
                            "A supervisor started in this session or another while ensure was deciding. Nothing was started.",
                        Remediation = "Run `heroesreplay services ensure` again.",
                        SupervisorAttached = false,
                    };
                }
            }

            ServiceEnsureReport result;
            // In JSON, the launch lines go to stderr so stdout is only the report.
            if (json)
            {
                Console.SetOut(Console.Error);
            }

            try
            {
                result = ensure.Apply(plan);
            }
            finally
            {
                Console.SetOut(stdout);
            }

            Write(result, json, stdout);
            busy.Dispose();
            busy = null;
            if (!result.Ok || !result.SupervisorAttached)
            {
                return result.ExitCode;
            }

            return Supervise(exe, cancellationToken);
        }
        finally
        {
            busy?.Dispose();
            claim?.Dispose();
        }
    }

    private static void Write(ServiceEnsureReport report, bool json, TextWriter output)
    {
        if (json)
        {
            output.WriteLine(report.ToJson());
            return;
        }

        ServiceEnsure.WriteText(output, report);
    }

    /// <summary>Spectate stays down: show the waiting scene on (or stop) a live stream this install started.</summary>
    private static string MakeObsSafe(ObsFailSafeAction action)
    {
        AppSettings app = ServiceCollectionExtensions.LoadAppSettings();
        return ObsFailSafe.Apply(
            action,
            app.OBS,
            new ObsStreamArm().IsArmed(),
            NamedProcess.IsRunning(ObsLaunchDecision.ProcessName),
            () =>
                new ObsWebsocketFailSafeSessionFactory().Open(
                    app.OBS?.WebSocketEndpoint,
                    app.OBS?.WebSocketPassword
                )
        );
    }

    /// <summary>The <c>services start</c> handshake: role prerequisites, kill, probe, and wait.</summary>
    private static ServiceStartupHandshake CreateHandshake(string exe)
    {
        ServiceStartupHandshake handshake;
        try
        {
            handshake = ServiceRoleStartup.ForCurrentProcess(exe);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(
                "Service startup failed: role configuration could not be loaded. " + e.Message
            );
            return null;
        }

        handshake.StopStarted = Kill;
        handshake.Probe = ServiceProcessProbe.TryFromProcess;
        handshake.Wait = Thread.Sleep;
        handshake.FindStarted = (pending, began) => FindStartedRole(exe, pending, began);
        return handshake;
    }

    /// <summary>
    /// The single-supervisor check, with <c>supervisor.json</c> as fresh as <c>services status</c>
    /// requires it.
    /// </summary>
    private static ServiceSupervisorGate SupervisorGate() =>
        new()
        {
            FreshFor = ServiceSupervisorFile.FreshFor(
                ServiceCollectionExtensions.LoadServiceHealthSettings()
            ),
        };

    /// <summary>
    /// After the stop file is down: wait for the supervisor to see it and exit, then kill it if it
    /// has not. Null when none was running.
    /// </summary>
    private static ServiceRoleStop StopSupervisor()
    {
        string path = ServiceSupervisorFile.DefaultPath;
        int pid = ServiceSupervisorFile.TryLoad(path)?.Pid ?? 0;
        try
        {
            if (!ServiceSupervisorFile.IsRunning())
            {
                return null;
            }

            if (WaitForSupervisorExit(SupervisorStopWait))
            {
                return new ServiceRoleStop(
                    ServiceRoleLog.SupervisorRole,
                    pid,
                    ServiceStopOutcome.Graceful
                );
            }

            string detail = null;
            if (pid > 0 && ServiceProcessPlan.IsHeroesReplay(ProcessNameOrNull(pid)))
            {
                try
                {
                    Kill(pid);
                }
                catch (Exception e)
                {
                    detail = "Kill failed: " + e.Message;
                }
            }
            else
            {
                detail = "supervisor.json names no live heroesreplay pid to kill.";
            }

            return WaitForSupervisorExit(TimeSpan.FromSeconds(5))
                ? new ServiceRoleStop(ServiceRoleLog.SupervisorRole, pid, ServiceStopOutcome.Killed)
                : new ServiceRoleStop(
                    ServiceRoleLog.SupervisorRole,
                    pid,
                    ServiceStopOutcome.StillRunning,
                    detail
                );
        }
        finally
        {
            if (!ServiceSupervisorFile.IsRunning())
            {
                ServiceSupervisorFile.Delete(path);
            }
        }
    }

    // A restart in progress ends its ready wait on the stop file; a launch takes up to 15 s more.
    private static readonly TimeSpan SupervisorStopWait = TimeSpan.FromSeconds(30);

    private static bool WaitForSupervisorExit(TimeSpan budget)
    {
        DateTimeOffset until = DateTimeOffset.UtcNow + budget;
        while (ServiceSupervisorFile.IsRunning())
        {
            if (DateTimeOffset.UtcNow >= until)
            {
                return false;
            }

            Thread.Sleep(200);
        }

        return true;
    }

    private static Command StopCommand()
    {
        var command = new Command(
            "stop",
            "Ask the recorded processes, and a spectate from this install that is not recorded, to shut down, kill any still running after 20 seconds, close Heroes of the Storm and any HeroesSwitcher left without it, and stop an OBS recording spectate left running (never the stream). Exits 1 unless every role exited, the game and those switchers closed, OBS is not streaming, and no recording spectate started is still running."
        );
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                ServiceStopResult result = ServiceSupervisor.Stop(
                    ServiceLockStore.DefaultPath,
                    new ServiceShutdown
                    {
                        ProcessNameOrNull = ProcessNameOrNull,
                        Probe = ServiceProcessProbe.TryFromProcess,
                        Kill = Kill,
                        RequestGracefulStop = () => ServiceStopFile.Request(),
                        GracefulWait = ServiceShutdown.DefaultGracefulWait,
                        Wait = Thread.Sleep,
                        ClearStopFile = () => ServiceStopFile.Clear(),
                        FindUnrecordedSpectates = recorded =>
                            UnrecordedSpectates.Find(
                                ProcessTable.Snapshot(),
                                ProcessCommandLine.TryRead,
                                Environment.ProcessPath,
                                Environment.ProcessId,
                                recorded
                            ),
                        GameRunning = () => NamedProcess.IsRunning(GameProcessName),
                        CloseGame = StopSpectatedGame,
                        CloseIdleSwitchers = new HeroesSwitcherShutdown().CloseIdle,
                        ConfirmStream = ObsServiceStop.DelegateToSpectator,
                        ReadStream = ServiceStreamProbe.Read,
                        StopSpectateRecording = ServiceRecordingProbe.StopLeftRecording,
                        StopSupervisor = StopSupervisor,
                    }
                );
                return Task.FromResult(result.ExitCode);
            }
        );
        return command;
    }

    private static Command StatusCommand()
    {
        var command = new Command(
            "status",
            "Classify each role as ready, degraded, stale, stopped, or failed from its heartbeat, with the cause and the fix, plus the spectator status file. Exits 1 when a role is failed, stale, or degraded."
        );
        Option<string> output = CliOutput.CreateOption(
            "JSON is a stable envelope: schemaVersion, ok, code (service.ready, service.degraded, service.stale, service.stopped, service.failed), roles[]."
        );
        command.Options.Add(output);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                ServiceHealthSettings health =
                    ServiceCollectionExtensions.LoadServiceHealthSettings();
                int code = ServiceSupervisor.Status(
                    ServiceLockStore.DefaultPath,
                    ProcessNameOrNull,
                    new SpectatorStatusStore().TryReadShared(),
                    ServiceProcessProbe.TryFromProcess,
                    new ServiceStatusQuery
                    {
                        Output = CliOutput.Format(parseResult, output),
                        Settings = health,
                        StopRequested = () => File.Exists(ServiceStopFile.DefaultPath),
                        Environment = Environment.GetEnvironmentVariable("HEROES_REPLAY_ENV"),
                        LogDirectory = ServiceCollectionExtensions
                            .LoadServiceLogSettings()
                            .ResolvedDirectory,
                        ReadSupervisor = () =>
                            ServiceSupervisorFile.TryLoad(ServiceSupervisorFile.DefaultPath),
                        SupervisorLiveness = () =>
                            ServiceSupervisorFile.Check(
                                freshFor: ServiceSupervisorFile.FreshFor(health)
                            ),
                        ReadMachine = ReadMachineHealth,
                        ReadObsRestorePending = () =>
                            ObsCollectionRollback.DescribePending(ObsManagedFiles.ForThisUser()),
                    }
                );
                return Task.FromResult(code);
            }
        );
        return command;
    }

    private static MachineHealthReport ReadMachineHealth()
    {
        MachineHealthSettings settings = ServiceCollectionExtensions.LoadMachineHealthSettings();
        return MachineHealth.Evaluate(MachineHealthProbe.Read(settings), settings);
    }

    private static string ProcessNameOrNull(int pid)
    {
        try
        {
            return Process.GetProcessById(pid).ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static string PowerShellStartCommand(
        string exe,
        string arguments,
        string pidFile,
        string serviceNonce = null,
        string serviceRole = null,
        string serviceVersion = null
    )
    {
        string argList = string.Join(
            ",",
            arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(PsQuote)
        );
        string prefix = string.Empty;
        if (!string.IsNullOrWhiteSpace(serviceNonce))
        {
            prefix =
                "$env:"
                + ServiceReadyFile.NonceVariable
                + "="
                + PsQuote(serviceNonce)
                + "; $env:"
                + ServiceReadyFile.RoleVariable
                + "="
                + PsQuote(serviceRole)
                + "; $env:"
                + ServiceReadyFile.VersionVariable
                + "="
                + PsQuote(serviceVersion)
                + "; ";
        }

        return prefix
            + "$ProgressPreference = 'SilentlyContinue'; $p = Start-Process -FilePath "
            + PsQuote(exe)
            + " -ArgumentList "
            + argList
            + " -WorkingDirectory "
            + PsQuote(Path.GetDirectoryName(exe))
            + " -WindowStyle Normal -PassThru; Set-Content -LiteralPath "
            + PsQuote(pidFile)
            + " -Value $p.Id -NoNewline";
    }

    private static int? StartProcess(
        string exe,
        string arguments,
        ServiceProcessRecord launch,
        TimeSpan? launcherWait = null,
        Func<bool> stopping = null
    )
    {
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "logs"
        );
        Directory.CreateDirectory(logDir);
        string slug = string.Join("-", arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        string pidFile = Path.Combine(logDir, slug + ".pid");
        if (File.Exists(pidFile))
        {
            File.Delete(pidFile);
        }

        string script = PowerShellStartCommand(
            exe,
            arguments,
            pidFile,
            launch?.Nonce,
            launch?.Name,
            launch?.Version
        );
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using Process process = Process.Start(
            new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " + encoded,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        );
        if (process == null)
        {
            return null;
        }

        // A stop request ends the wait early; what the launcher started is still looked for.
        DateTimeOffset until = DateTimeOffset.UtcNow + (launcherWait ?? DefaultLauncherWait);
        bool exited = false;
        while (!(exited = process.WaitForExit(500)))
        {
            if (DateTimeOffset.UtcNow >= until || stopping?.Invoke() == true)
            {
                break;
            }
        }

        if (!exited)
        {
            try
            {
                process.Kill();
                process.WaitForExit(2000);
            }
            catch (InvalidOperationException) { }
        }

        // Start-Process may have started the role, and even written its pid, before the
        // launcher was cut off or failed (#397): the pid file still counts then.
        int? pid = File.Exists(pidFile) ? ParseProcessId(ReadPidFile(pidFile)) : null;
        if (pid == null)
        {
            return null;
        }

        if (!exited || process.ExitCode != 0)
        {
            Console.Error.WriteLine(
                $"The launcher for {arguments} did not finish cleanly, but it wrote pid {pid}."
            );
        }

        Console.WriteLine($"Console open for {arguments} (pid file {pidFile}).");
        return pid;
    }

    private static string ReadPidFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static int? ParseProcessId(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        string[] lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (int.TryParse(lines[i].Trim(), out int pid) && pid > 0)
            {
                return pid;
            }
        }

        return null;
    }

    private static void PatchObsCollection(string exe)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exe))
            {
                return;
            }

            string installDirectory = Path.GetDirectoryName(exe);
            (OBSSettings obs, string dataDirectory) =
                ServiceCollectionExtensions.LoadInstallObsSettings(
                    installDirectory,
                    Environment.GetEnvironmentVariable("HEROES_REPLAY_ENV")
                );
            ObsCollectionApplyResult result = ObsCollectionPatcher.ApplyForInstall(
                installDirectory,
                dataDirectory,
                Process.GetProcessesByName("obs64").Length > 0,
                ObsNames.SceneCollection(obs),
                ObsManagedFiles.ForThisUser(),
                obs?.StableAssets == true,
                ObsRuntimeValues.From(obs)
            );
            if (result.Drift || result.Wrote || result.Deferred)
            {
                Console.WriteLine(result.Message);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("OBS collection was not updated. " + e.Message);
        }
    }

    private static string PsQuote(string value) => "'" + (value ?? "").Replace("'", "''") + "'";

    private const string GameProcessName = "HeroesOfTheStorm_x64";

    private static bool StopSpectatedGame()
    {
        foreach (Process game in Process.GetProcessesByName(GameProcessName))
        {
            using (game)
            {
                try
                {
                    game.Kill(entireProcessTree: true);
                    game.WaitForExit(5000);
                    Console.WriteLine($"Stopped Heroes of the Storm pid {game.Id}.");
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(
                        $"Could not stop Heroes of the Storm pid {game.Id}: {e.Message}"
                    );
                }
            }
        }

        Process[] left = Process.GetProcessesByName(GameProcessName);
        foreach (Process game in left)
        {
            game.Dispose();
        }

        return left.Length == 0;
    }

    private static void Kill(int pid)
    {
        using Process process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: true);
        process.WaitForExit(5000);
    }
}
