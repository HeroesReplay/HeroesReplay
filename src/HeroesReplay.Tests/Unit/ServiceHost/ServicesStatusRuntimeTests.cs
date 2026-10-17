using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.ServiceHost;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary><c>services status</c> names the .NET runtime (#462).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServicesStatusRuntimeTests
{
    [Fact]
    public void Status_NamesTheRuntimeARoleStartedNowLoads()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"heroesreplay-services-{Guid.NewGuid():N}.json"
        );
        try
        {
            var text = new StringWriter();
            ServiceSupervisor.Status(
                path,
                pid => null,
                spectator: null,
                query: new ServiceStatusQuery { Out = text }
            );
            Assert.Contains(
                $"  Runtime: {RuntimeInformation.FrameworkDescription} for a role started now",
                text.ToString(),
                StringComparison.Ordinal
            );

            var json = new StringWriter();
            ServiceSupervisor.Status(
                path,
                pid => null,
                spectator: null,
                query: new ServiceStatusQuery { Output = CliOutputFormat.Json, Out = json }
            );
            using var document = JsonDocument.Parse(json.ToString());
            Assert.Equal(
                RuntimeInformation.FrameworkDescription,
                document.RootElement.GetProperty("dotnetRuntime").GetString()
            );
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void StatusText_ShowsEachRolesRuntimeAndTheRelease()
    {
        var report = new ServiceStatusReport
        {
            Ok = true,
            Code = ServiceHealthCodes.Ready,
            Message = "1 ready.",
            CheckedAt = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero),
            DotnetRuntime = ".NET 10.0.13",
            ReleaseRuntime = ".NET 10.0.13",
            Roles =
            [
                new ServiceRoleHealth
                {
                    Role = "download",
                    State = ServiceRoleState.Ready,
                    Code = ServiceHealthCodes.Ready,
                    Cause = "Heartbeating.",
                    Expected = true,
                    Running = true,
                    Pid = 4242,
                    Version = "1.2.3",
                    DotnetRuntime = ".NET 10.0.12",
                },
            ],
        };

        var text = new StringWriter();
        ServiceSupervisor.WriteStatusText(text, report, spectator: null);

        string output = text.ToString();
        Assert.Contains(
            "  Runtime: .NET 10.0.13 for a role started now; this release was built and tested on .NET 10.0.13 (runtime.txt).",
            output,
            StringComparison.Ordinal
        );
        Assert.Contains("pid 4242, version 1.2.3, .NET 10.0.12", output, StringComparison.Ordinal);
    }
}
