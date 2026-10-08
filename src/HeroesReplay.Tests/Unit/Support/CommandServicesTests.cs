using System;
using System.IO;
using System.Threading;
using HeroesReplay.CLI;
using HeroesReplay.CLI.Commands.HeroesProfile.Commands;
using HeroesReplay.CLI.Commands.Twitch.Commands;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.GameClient.Firewall;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Spectating.Reports;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.Twitch.Predictions;
using HeroesReplay.Core.Twitch.RedeemedRewards;
using HeroesReplay.Core.Twitch.Rewards;
using HeroesReplay.Core.TwitchExtension;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Playlists;
using Microsoft.Extensions.DependencyInjection;
using OBSWebsocketDotNet;
using TwitchLib.Api.Interfaces;
using TwitchLib.Client.Interfaces;
using Xunit;

namespace HeroesReplay.Tests.Unit.Support;

/// <summary>
/// Each command that builds its own service provider can resolve the services it asks for
/// (#297: <c>twitch predictions test</c> missed <see cref="PredictionReportWriter"/>). Nothing is
/// connected or started. The data directory is a temp folder, so a constructor that writes a page
/// (the request queue writes its board) does not touch the machine's Data folder. Spectate is not
/// here: its engine builds the game controller, capture, and the OBS controller.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class CommandServicesTests
{
    [Theory]
    [InlineData("twitch predictions test")]
    [InlineData("twitch connect")]
    [InlineData("twitch say")]
    [InlineData("twitch rewards test")]
    [InlineData("twitch rewards submit, list, generate, remove-unranked-draft")]
    [InlineData("heroesprofile download")]
    [InlineData("heroesprofile sample, patch-index")]
    [InlineData("youtube uploader")]
    [InlineData("youtube library")]
    [InlineData("check")]
    [InlineData("client")]
    [InlineData("calculators report")]
    public void Command_ResolvesItsRootServices(string command)
    {
        CancellationToken token = CancellationToken.None;
        (IServiceCollection services, Type[] roots) = command switch
        {
            "twitch predictions test" => (
                PredictionsTestCommand.AddServices(new ServiceCollection(), token),
                new[] { typeof(IMatchPredictionService) }
            ),
            "twitch connect" => (
                new ServiceCollection().AddTwitchServices(token),
                new[]
                {
                    typeof(IGameData),
                    typeof(ITwitchRewardsManager),
                    typeof(ITwitchBot),
                    typeof(StatusPredictionWatcher),
                    typeof(RedemptionFulfiller),
                }
            ),
            "twitch say" => (
                new ServiceCollection().AddTwitchServices(token),
                new[] { typeof(IGameData), typeof(ITwitchBot), typeof(ITwitchClient) }
            ),
            "twitch rewards test" => (
                new ServiceCollection().AddTwitchServices(token),
                new[]
                {
                    typeof(IGameData),
                    typeof(ITwitchBot),
                    typeof(ITwitchClient),
                    typeof(IOnRewardHandler),
                }
            ),
            "twitch rewards submit, list, generate, remove-unranked-draft" => (
                new ServiceCollection().AddTwitchServices(token),
                new[] { typeof(IGameData), typeof(ITwitchRewardsManager) }
            ),
            "heroesprofile download" => (
                DownloadCommand.AddServices(new ServiceCollection(), token),
                new[] { typeof(HeroesProfileProvider) }
            ),
            "heroesprofile sample, patch-index" => (
                new ServiceCollection().AddTwitchServices(token, "heroesreplay-sample"),
                new[] { typeof(IHeroesProfileService) }
            ),
            "youtube uploader" => (
                new ServiceCollection().AddYouTubeServices(token),
                new[] { typeof(IYouTubeUploader) }
            ),
            "youtube library" => (
                new ServiceCollection().AddYouTubeServices(token),
                new[] { typeof(IYouTubeLibrary) }
            ),
            "check" => (
                new ServiceCollection().AddCheckServices(token),
                new[]
                {
                    typeof(IHeroesProfileService),
                    typeof(OBSWebsocket),
                    typeof(ITwitchAPI),
                    typeof(IConnectivityWatchdog),
                    typeof(ITwitchExtensionService),
                    typeof(StormClientConfigurator),
                }
            ),
            "client" => (
                new ServiceCollection().AddClientServices(),
                new[] { typeof(StormClientConfigurator), typeof(IGameFirewall) }
            ),
            "calculators report" => (
                new ServiceCollection().AddReportServices(token, typeof(ReplayFileProvider)),
                new[] { typeof(ISpectateReportWriter) }
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
        string data = Directory.CreateTempSubdirectory("hr-command-services-").FullName;
        try
        {
            UseDataDirectory(services, data);
            using ServiceProvider provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
            using IServiceScope scope = provider.CreateScope();

            foreach (Type root in roots)
            {
                Assert.NotNull(scope.ServiceProvider.GetRequiredService(root));
            }
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    // Every AppSettings the command registers points at data, whether it is an instance or a
    // factory.
    private static void UseDataDirectory(IServiceCollection services, string data)
    {
        for (int i = 0; i < services.Count; i++)
        {
            ServiceDescriptor descriptor = services[i];
            if (descriptor.ServiceType != typeof(AppSettings))
            {
                continue;
            }

            if (descriptor.ImplementationInstance is AppSettings settings)
            {
                settings.Location.DataDirectory = data;
                continue;
            }

            Func<IServiceProvider, object> factory = descriptor.ImplementationFactory;
            services[i] = ServiceDescriptor.Singleton(serviceProvider =>
            {
                var bound = (AppSettings)factory(serviceProvider);
                bound.Location.DataDirectory = data;
                return bound;
            });
        }
    }
}
