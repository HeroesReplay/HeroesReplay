using System;
using System.Collections.Generic;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceMachineShutdownTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(10);

    private static ServiceStopResult Stopped() =>
        new()
        {
            Roles = [new ServiceRoleStop("spectate", 10, ServiceStopOutcome.Graceful)],
            GameClosed = true,
            // services stop fails on a live stream; shutdown closes OBS next, so it does not count.
            Stream = ServiceStreamCheck.Active(),
        };

    private static ServiceStopResult RoleLeft() =>
        new()
        {
            Roles = [new ServiceRoleStop("spectate", 10, ServiceStopOutcome.StillRunning)],
            GameClosed = true,
        };

    private sealed class Fake
    {
        public List<int> Obs { get; } = new();
        public HashSet<int> IgnoresClose { get; } = new();
        public List<string> Steps { get; } = new();
        public int PowerExit { get; set; }
        public ServiceStopResult Stack { get; set; } = Stopped();

        public ServiceMachineShutdown Shutdown() =>
            new()
            {
                StopStack = () =>
                {
                    Steps.Add("stop");
                    return Stack;
                },
                FindObs = () => Obs.ToArray(),
                StopObsOutputs = () =>
                {
                    Steps.Add("outputs");
                    return new ObsOutputsStop(true, true, true, "stream and recording stopped.");
                },
                CloseObs = pid =>
                {
                    Steps.Add("close " + pid);
                    if (!IgnoresClose.Contains(pid))
                    {
                        Obs.Remove(pid);
                    }
                },
                WaitForExit = (pid, _) => !Obs.Contains(pid),
                KillObs = pid =>
                {
                    Steps.Add("kill " + pid);
                    Obs.Remove(pid);
                },
                RequestPower = (action, delay) =>
                {
                    Steps.Add($"power {action} {delay.TotalSeconds}");
                    return PowerExit;
                },
                Write = _ => { },
            };
    }

    [Fact]
    public void Restart_StopsTheStackThenOutputsThenOBS_ThenAsksWindows()
    {
        var fake = new Fake();
        fake.Obs.Add(42);

        ServiceMachineShutdownResult result = fake
            .Shutdown()
            .Run(MachinePowerAction.Restart, Delay, force: false);

        Assert.Equal(new[] { "stop", "outputs", "close 42", "power Restart 10" }, fake.Steps);
        Assert.Equal(ObsCloseOutcome.Closed, result.Obs);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void HungObs_IsKilledAfterTheCloseWait()
    {
        var fake = new Fake();
        fake.Obs.Add(42);
        fake.IgnoresClose.Add(42);

        ServiceMachineShutdownResult result = fake
            .Shutdown()
            .Run(MachinePowerAction.None, Delay, force: false);

        Assert.Equal(new[] { "stop", "outputs", "close 42", "kill 42" }, fake.Steps);
        Assert.Equal(ObsCloseOutcome.Killed, result.Obs);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void NoObs_SkipsTheWebsocket()
    {
        var fake = new Fake();

        ServiceMachineShutdownResult result = fake
            .Shutdown()
            .Run(MachinePowerAction.PowerOff, Delay, force: false);

        Assert.Equal(new[] { "stop", "power PowerOff 10" }, fake.Steps);
        Assert.Equal(ObsCloseOutcome.NotRunning, result.Obs);
    }

    [Fact]
    public void RoleStillRunning_LeavesObsAndWindowsAlone()
    {
        var fake = new Fake { Stack = RoleLeft() };
        fake.Obs.Add(42);

        ServiceMachineShutdownResult result = fake
            .Shutdown()
            .Run(MachinePowerAction.Restart, Delay, force: false);

        Assert.Equal(new[] { "stop" }, fake.Steps);
        Assert.Equal(ObsCloseOutcome.Skipped, result.Obs);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void Force_ClosesObsAndRestartsAnyway_ButStillFails()
    {
        var fake = new Fake { Stack = RoleLeft() };
        fake.Obs.Add(42);

        ServiceMachineShutdownResult result = fake
            .Shutdown()
            .Run(MachinePowerAction.Restart, Delay, force: true);

        Assert.Equal(new[] { "stop", "outputs", "close 42", "power Restart 10" }, fake.Steps);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void ShutdownExeFailing_FailsTheCommand()
    {
        var fake = new Fake { PowerExit = 1190 };

        Assert.Equal(
            1,
            fake.Shutdown().Run(MachinePowerAction.Restart, Delay, force: false).ExitCode
        );
    }
}
