using System;
using System.IO;
using System.Text.Json;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.ServiceHost;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary><c>services status</c> names the processes holding the commit (#399).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServicesStatusMachineTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    [Fact]
    public void Status_ListsTheTopConsumersAboveTheCommitLimitWithoutChangingTheExitCode()
    {
        string path = TempServices();
        try
        {
            MachineHealthReport machine = Machine(commitGb: 24.2);
            var text = new StringWriter();
            int code = ServiceSupervisor.Status(
                path,
                pid => null,
                spectator: null,
                query: new ServiceStatusQuery { Out = text, ReadMachine = () => machine }
            );

            Assert.Equal(0, code);
            string output = text.ToString();
            Assert.Contains("WARN Commit charge is 96.8% of the limit", output);
            Assert.Contains("  Top 3 processes by commit (private bytes), report only:", output);
            Assert.Contains(
                "    msedge pid 18080: 13188 MB private, 10 MB working set, started ",
                output
            );
            Assert.True(
                output.IndexOf("msedge pid 18080", StringComparison.Ordinal)
                    < output.IndexOf("obs64 pid 7012", StringComparison.Ordinal)
            );

            var json = new StringWriter();
            ServiceSupervisor.Status(
                path,
                pid => null,
                spectator: null,
                query: new ServiceStatusQuery
                {
                    Output = CliOutputFormat.Json,
                    Out = json,
                    ReadMachine = () => machine,
                }
            );
            using var document = JsonDocument.Parse(json.ToString());
            JsonElement top = document
                .RootElement.GetProperty("machine")
                .GetProperty("topConsumers");
            Assert.Equal(3, top.GetArrayLength());
            Assert.Equal("msedge", top[0].GetProperty("name").GetString());
            Assert.Equal(18080, top[0].GetProperty("pid").GetInt32());
            Assert.Equal(13188, top[0].GetProperty("privateMegabytes").GetInt64());
            Assert.Equal(10, top[0].GetProperty("workingSetMegabytes").GetInt64());
            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Status_ListsNoConsumersInsideTheLimits()
    {
        string path = TempServices();
        try
        {
            var text = new StringWriter();
            ServiceSupervisor.Status(
                path,
                pid => null,
                spectator: null,
                query: new ServiceStatusQuery
                {
                    Out = text,
                    ReadMachine = () => Machine(commitGb: 12),
                }
            );

            string output = text.ToString();
            Assert.Contains("Machine: memory 50%", output);
            Assert.DoesNotContain("Top ", output);
            Assert.DoesNotContain("msedge", output);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    private static MachineHealthReport Machine(double commitGb) =>
        MachineHealth.Evaluate(
            new MachineHealthSnapshot
            {
                PhysicalTotalBytes = 16 * Gigabyte,
                PhysicalAvailableBytes = 8 * Gigabyte,
                CommitBytes = (long)(commitGb * Gigabyte),
                CommitLimitBytes = 25 * Gigabyte,
                AgentProcesses = 1,
                ConhostProcesses = 12,
                HeroesProcesses = 1,
                AllProcesses = new[]
                {
                    Memory("obs64", 7012, 1450, 900),
                    Memory("heroesreplay", 21960, 999, 410),
                    new MachineProcessMemory
                    {
                        Name = "msedge",
                        Pid = 18080,
                        PrivateMegabytes = 13188,
                        WorkingSetMegabytes = 10,
                        StartedAt = new DateTimeOffset(2026, 10, 8, 10, 52, 0, TimeSpan.Zero),
                    },
                    Memory("svchost", 1200, 60, 20),
                },
            },
            new MachineHealthSettings { TopConsumerCount = 3 }
        );

    private static MachineProcessMemory Memory(
        string name,
        int pid,
        long privateMegabytes,
        long workingSetMegabytes
    ) =>
        new()
        {
            Name = name,
            Pid = pid,
            PrivateMegabytes = privateMegabytes,
            WorkingSetMegabytes = workingSetMegabytes,
        };

    private static string TempServices() =>
        Path.Combine(Path.GetTempPath(), $"heroesreplay-services-{Guid.NewGuid():N}.json");
}
