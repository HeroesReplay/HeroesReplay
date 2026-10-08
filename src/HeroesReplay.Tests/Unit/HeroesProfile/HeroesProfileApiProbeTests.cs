using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Kiota.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

/// <summary>#305: the download role's probe is one Heroes Profile replay-list call.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesProfileApiProbeTests
{
    private const string Key = "hp-secret-key-0123456789";

    [Fact]
    public async Task AnAnsweredList_IsOk()
    {
        ServiceDependencyResult result = await Probe(_ => Task.FromResult<int?>(65550003))
            .CheckAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Ok, result.State);
        Assert.Null(result.Code);
        Assert.Contains("65550003", result.Cause);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task ARefusedKey_IsRejected(int status)
    {
        ServiceDependencyResult result = await Probe(_ =>
                throw new ApiException("unauthenticated") { ResponseStatusCode = status }
            )
            .CheckAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Rejected, result.State);
        Assert.Equal(HeroesProfileApiProbe.RejectedCode, result.Code);
        Assert.Contains($"HTTP {status}", result.Cause);
        Assert.Contains("fill-secrets-from-op.ps1", result.Remediation);
        Assert.Contains("does not restart it", result.Remediation);
        Assert.DoesNotContain(Key, result.Cause);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task AServerErrorOrRateLimit_IsUnreachable(int status)
    {
        ServiceDependencyResult result = await Probe(_ =>
                throw new ApiException("busy") { ResponseStatusCode = status }
            )
            .CheckAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Unreachable, result.State);
        Assert.Equal(HeroesProfileApiProbe.UnreachableCode, result.Code);
        Assert.Contains($"HTTP {status}", result.Cause);
    }

    [Fact]
    public async Task NoNetwork_IsUnreachable()
    {
        ServiceDependencyResult result = await Probe(_ =>
                throw new HttpRequestException("No such host is known. (www.heroesprofile.com:443)")
            )
            .CheckAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Unreachable, result.State);
        Assert.Equal(HeroesProfileApiProbe.UnreachableCode, result.Code);
        Assert.Contains("No such host", result.Cause);
    }

    [Fact]
    public async Task AnEmptyKey_IsRejectedWithoutACall()
    {
        bool called = false;
        var probe = new HeroesProfileApiProbe(
            new HeroesProfileApiSettings { ApiKey = " " },
            _ =>
            {
                called = true;
                return Task.FromResult<int?>(1);
            }
        );

        ServiceDependencyResult result = await probe.CheckAsync(CancellationToken.None);

        Assert.False(called);
        Assert.Equal(HeroesProfileApiProbe.RejectedCode, result.Code);
    }

    [Fact]
    public async Task AnOutageThatHangs_IsUnreachableThroughTheMonitor_NotAFailedRole()
    {
        var monitor = new ServiceDependencyMonitor(
            Probe(async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return 1;
            }),
            new ServiceHealthSettings { DependencyProbeTimeout = TimeSpan.FromSeconds(1) },
            serviceRole: true
        );

        ServiceDependencyResult result = await monitor.FirstAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Unreachable, result.State);
        Assert.Equal(HeroesProfileApiProbe.UnreachableCode, result.Code);
        Assert.Contains("Heroes Profile API did not answer within 1s", result.Cause);
    }

    private static HeroesProfileApiProbe Probe(Func<CancellationToken, Task<int?>> read) =>
        new(new HeroesProfileApiSettings { ApiKey = Key, MinReplayId = 65267450 }, read);
}
