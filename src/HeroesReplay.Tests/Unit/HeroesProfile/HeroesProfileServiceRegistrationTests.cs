using System;
using System.Threading;
using HeroesReplay.CLI;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesProfileServiceRegistrationTests
{
    [Fact]
    public void AddHeroesProfileService_ResolvesWithoutHttpClientConstructor()
    {
        var settings = new AppSettings
        {
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                ApiKey = "unit-test-key",
                ExternalV1BaseUri = new Uri("http://127.0.0.1:9/"),
            },
        };
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddSingleton(new CancellationTokenProvider(CancellationToken.None));
        services.AddSingleton(settings);
        services.AddSingleton<ILogger<HeroesProfileService>>(
            NullLogger<HeroesProfileService>.Instance
        );
        services.AddHeroesProfileService();

        using ServiceProvider provider = services.BuildServiceProvider();
        IHeroesProfileService resolved = provider.GetRequiredService<IHeroesProfileService>();

        Assert.IsType<HeroesProfileService>(resolved);
    }
}
