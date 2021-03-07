
using Heroes.ReplayParser;

using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Analysis;
using HeroesReplay.Core.Services.Context;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.HeroesProfileExtension;
using HeroesReplay.Core.Services.Observer;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.Core.Services.Providers;
using HeroesReplay.Core.Services.Queue;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using OBSWebsocketDotNet;

using Polly.Caching;
using Polly.Caching.Memory;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Windows.Media.Ocr;

namespace HeroesReplay.Service.Observer
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            using (var cts = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (s, e) => { e.Cancel = false; cts.Cancel(); };

                IHostBuilder builder = Host
                   .CreateDefaultBuilder()
                   .ConfigureServices(services => services.AddSingleton(cts))
                   .ConfigureServices(ConfigureServices)
                   .ConfigureAppConfiguration(ConfigureAppConfig);

                using (IHost host = builder.Build())
                {
                    using (var scope = host.Services.CreateScope())
                    {
                        var data = scope.ServiceProvider.GetRequiredService<IGameData>();

                        await data.LoadDataAsync();
                    }

                    await host.RunAsync();
                }
            }
        }

        private static void ConfigureAppConfig(HostBuilderContext context, IConfigurationBuilder builder)
        {
            builder.AddJsonFile("appsettings.Secrets.json", optional: true);
        }

        private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            services.AddOptions();
            services.Configure<AppSettings>(context.Configuration);
            services.PostConfigure<AppSettings>(PostConfigure);

            foreach (var type in typeof(IFocusCalculator).Assembly.GetTypes().Where(type => type.IsClass && typeof(IFocusCalculator).IsAssignableFrom(type)))
            {
                services.AddSingleton(typeof(IFocusCalculator), type);
            }

            if (context.HostingEnvironment.IsDevelopment())
            {
                services
                   .AddSingleton<CaptureStrategy, StubCapture>()
                   .AddSingleton<IGameController, StubController>()
                   .AddSingleton<ITalentNotifier, StubNotifier>();
            }
            else
            {
                services.AddHttpClient<TwitchExtensionService>();

                services
                    .AddSingleton(OcrEngine.TryCreateFromUserProfileLanguages())
                    .AddSingleton<CaptureStrategy, BitBltCapture>()
                    .AddSingleton<IGameController, GameController>()
                    .AddSingleton<ITalentNotifier, TalentNotifier>()
                    .AddSingleton<ITwitchExtensionService, TwitchExtensionService>();
            }

            services.AddHttpClient<HeroesProfileService>();

            services
                .AddMemoryCache()
                .AddSingleton<IAsyncCacheProvider, MemoryCacheProvider>()
                .AddSingleton<IReplayProvider, HeroesProfileProvider>()
                .AddSingleton<IGameData, GameData>()
                .AddSingleton<IReplayHelper, ReplayHelper>()
                .AddSingleton<IAbilityDetector, AbilityDetector>()
                .AddSingleton<IGameManager, GameManager>()
                .AddSingleton<IReplayAnalyzer, ReplayAnalyzer>()
                .AddSingleton<ISpectator, Spectator>()
                .AddSingleton<IReplayLoader, ReplayLoader>()
                .AddSingleton<IReplayContext, ReplayContext>()
                .AddSingleton<IHeroesProfileService, HeroesProfileService>()
                .AddSingleton<IPayloadsBuilder, PayloadsBuilder>()
                .AddSingleton<IContextManager, ContextManager>()
                .AddSingleton<IRequestQueueDequeuer, RequestQueueDequeuer>()
                .AddSingleton<OBSWebsocket>()
                .AddSingleton<IObsController, ObsController>()
                .AddHostedService<ObserverService>();
        }

        public static void PostConfigure(AppSettings appSettings)
        {
            appSettings.CurrentDirectory = Directory.GetCurrentDirectory();
            appSettings.AssetsPath = Path.Combine(appSettings.CurrentDirectory, "Assets");
            appSettings.ContextsDirectory = Path.Combine(appSettings.Location.DataDirectory, "Contexts");
            appSettings.HeroesDataPath = Path.Combine(appSettings.Location.DataDirectory, "HeroesData");
            appSettings.StandardReplayCachePath = Path.Combine(appSettings.Location.DataDirectory, appSettings.HeroesProfileApi.StandardCacheDirectoryName);
            appSettings.RequestedReplayCachePath = Path.Combine(appSettings.Location.DataDirectory, appSettings.HeroesProfileApi.RequestsCacheDirectoryName);
            appSettings.SpectateReportPath = Path.Combine(appSettings.Location.DataDirectory, "SpectateReport");
            appSettings.CapturesPath = Path.Combine(appSettings.Location.DataDirectory, "Capture");
            appSettings.StormReplaysAccountPath = Path.Combine(appSettings.UserGameFolderPath, "Accounts");
            appSettings.UserStormInterfacePath = Path.Combine(appSettings.UserGameFolderPath, "Interfaces");
        }
    }
}
