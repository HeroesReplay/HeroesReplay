using System.Threading.Tasks;
using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.Core.SelfUpdate;
using Xunit;

namespace HeroesReplay.Tests.Unit.Check;

/// <summary><c>check runtime</c> (#462).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class RuntimeCheckResultTests
{
    [Fact]
    public void TheReleaseRuntime_IsOk()
    {
        CheckCommand.CheckResult result = CheckCommand.ToCheckResult(
            ReleaseRuntime.Evaluate(".NET 10.0.13", @"C:\rt", ".NET 10.0.13")
        );

        Assert.Equal("runtime", result.Name);
        Assert.True(result.Ok);
        Assert.Equal(CheckCodes.RuntimeOk, result.Code);
        Assert.Equal(CheckEntry.Pass, CheckEntry.From(result).Status);
    }

    [Fact]
    public void AnOlderMachineRuntime_PassesAsAWarningThatStillStartsWithTheRuntime()
    {
        CheckCommand.CheckResult result = CheckCommand.ToCheckResult(
            ReleaseRuntime.Evaluate(".NET 10.0.12", @"C:\rt", ".NET 10.0.13")
        );

        Assert.True(result.Ok);
        Assert.Equal("check.runtime.older_than_release", result.Code);
        CheckEntry entry = CheckEntry.From(result);
        Assert.Equal(CheckEntry.Warn, entry.Status);
        Assert.StartsWith(".NET 10.0.12, from", entry.Detail);
    }

    [Fact]
    public async Task TheTarget_ReportsThisProcesssRuntime()
    {
        CheckCommand.CheckResult result = await CheckCommand.RunTargetAsync("runtime", default);

        Assert.True(result.Ok);
        Assert.StartsWith(ReleaseRuntime.Current + ", from ", CheckEntry.From(result).Detail);
    }
}
