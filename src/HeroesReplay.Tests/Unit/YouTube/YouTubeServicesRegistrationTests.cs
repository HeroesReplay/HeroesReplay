using System.Threading;
using HeroesReplay.CLI;
using HeroesReplay.Core.YouTube.Playlists;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeServicesRegistrationTests
{
    [Fact]
    public void AddYouTubeServices_ResolvesTheLibraryWithHeroesProfile()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddYouTubeServices(CancellationToken.None)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );

        Assert.IsType<YouTubeLibrary>(provider.GetRequiredService<IYouTubeLibrary>());
    }
}
