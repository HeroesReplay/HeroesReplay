using System.Text.Json;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary><c>services.json</c>, the ready files and <c>supervisor.json</c> keep their bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceHostJsonTests
{
    private static readonly JsonSerializerOptions Before = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void ServicesJson_KeepsItsBytes() =>
        JsonParity.AssertSame(Before, ServiceLockStore.Json.ServiceLock);

    [Fact]
    public void ReadyFile_KeepsItsBytes() =>
        JsonParity.AssertSame(Before, ServiceReadyFile.Json.ServiceReadyReport);

    [Fact]
    public void SupervisorJson_KeepsItsBytes() =>
        JsonParity.AssertSame(Before, ServiceSupervisorFile.Json.ServiceSupervisorState);
}
