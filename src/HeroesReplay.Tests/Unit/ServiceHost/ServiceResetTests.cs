using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.ServiceHost;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceResetTests
{
    [Fact]
    public void ResetFile_QueuesEachRoleOnce_AndTakeEmptiesIt()
    {
        string root = Path.Combine(Path.GetTempPath(), "heroesreplay-reset-" + Guid.NewGuid());
        string path = Path.Combine(root, "services.reset");
        try
        {
            Assert.Empty(ServiceResetFile.Take(path));
            ServiceResetFile.Request("download", path);
            ServiceResetFile.Request("Download", path);
            ServiceResetFile.Request("youtube", path);

            Assert.True(ServiceResetFile.Pending("download", path));
            Assert.False(ServiceResetFile.Pending("twitch", path));
            Assert.Equal(new[] { "download", "youtube" }, ServiceResetFile.Take(path));
            Assert.False(File.Exists(path));
            Assert.False(ServiceResetFile.Pending("download", path));
            Assert.Empty(ServiceResetFile.Take(path));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ResetPolicy_GivesTheWholeBudgetBack_AndAFailedRoleIsScheduledAgain()
    {
        var settings = new ServiceRestartSettings();
        DateTimeOffset now = new(2026, 10, 20, 10, 5, 0, TimeSpan.Zero);
        var ledger = new ServiceRoleRestarts { Role = "download", Count = 5 };
        for (int index = 0; index < 5; index++)
        {
            ledger.Recent.Add(now.AddMinutes(-20 + index));
        }

        var failed = new ServiceRoleHealth { Role = "download", State = ServiceRoleState.Failed };
        Assert.Equal(
            ServiceRestartAction.Exhausted,
            ServiceRestartPolicy.Decide(failed, ledger, now, settings)
        );
        Assert.Equal(
            ServiceRestartAction.None,
            ServiceRestartPolicy.Decide(failed, ledger, now.AddHours(1), settings)
        );

        ServiceRestartPolicy.Reset(ledger, now.AddHours(1));

        Assert.False(ledger.Exhausted);
        Assert.Null(ledger.ExhaustedAt);
        Assert.Empty(ledger.Recent);
        Assert.Equal(5, ledger.Count);
        Assert.Equal(1, ledger.Resets);
        Assert.Equal(
            ServiceRestartAction.Scheduled,
            ServiceRestartPolicy.Decide(failed, ledger, now.AddHours(1), settings)
        );
        Assert.Equal(now.AddHours(1).AddSeconds(10), ledger.NextRestartAt);
    }

    [Fact]
    public void Reset_QueuesTheRole_AndSucceedsOnceTheSupervisorTakesIt()
    {
        var queued = new List<string>();
        int checks = 0;
        var output = new StringWriter();
        var error = new StringWriter();

        int code = ServiceSupervisor.Reset(
            "Download",
            new ServiceResetRequest
            {
                SupervisorLiveness = () => new ServiceSupervisorLiveness { Running = true },
                Request = queued.Add,
                Pending = _ => ++checks < 3,
                Wait = _ => { },
            },
            output,
            error
        );

        Assert.Equal(0, code);
        Assert.Equal(new[] { "download" }, queued);
        Assert.Contains("gave download its restart budget back", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void Reset_WithoutASupervisor_QueuesNothing_AndNamesEnsure()
    {
        var queued = new List<string>();
        var error = new StringWriter();

        int code = ServiceSupervisor.Reset(
            "youtube",
            new ServiceResetRequest
            {
                SupervisorLiveness = () =>
                    ServiceSupervisorLiveness.NotRunning("no supervisor.json"),
                Request = queued.Add,
                Pending = _ => false,
                Wait = _ => { },
            },
            TextWriter.Null,
            error
        );

        Assert.Equal(1, code);
        Assert.Empty(queued);
        Assert.Contains("services ensure --roles youtube", error.ToString());
        Assert.Contains("no supervisor.json", error.ToString());
    }

    [Fact]
    public void Reset_RejectsAnUnknownRole()
    {
        var queued = new List<string>();
        var error = new StringWriter();

        int code = ServiceSupervisor.Reset(
            "obs",
            new ServiceResetRequest
            {
                SupervisorLiveness = () => new ServiceSupervisorLiveness { Running = true },
                Request = queued.Add,
                Pending = _ => false,
                Wait = _ => { },
            },
            TextWriter.Null,
            error
        );

        Assert.Equal(1, code);
        Assert.Empty(queued);
        Assert.Contains("spectate, twitch, download, youtube", error.ToString());
    }

    [Fact]
    public void Reset_ThatTheSupervisorDoesNotTake_StaysQueuedAndExitsOne()
    {
        var waited = TimeSpan.Zero;
        var error = new StringWriter();

        int code = ServiceSupervisor.Reset(
            "twitch",
            new ServiceResetRequest
            {
                SupervisorLiveness = () => new ServiceSupervisorLiveness { Running = true },
                Request = _ => { },
                Pending = _ => true,
                Wait = pause => waited += pause,
            },
            TextWriter.Null,
            error
        );

        Assert.Equal(1, code);
        Assert.Equal(TimeSpan.FromSeconds(15), waited);
        Assert.Contains("stays queued", error.ToString());
    }
}
