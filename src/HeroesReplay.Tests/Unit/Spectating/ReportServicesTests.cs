using System.Threading;
using HeroesReplay.CLI;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Spectating.Reports;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReportServicesTests
{
    [Fact]
    public void AddReportServices_ResolvesTheReportWriter()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddReportServices(CancellationToken.None, typeof(ReplayFileProvider))
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );

        Assert.NotNull(provider.GetRequiredService<ISpectateReportWriter>());
    }
}
