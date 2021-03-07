using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using OBSWebsocketDotNet;

namespace HeroesReplay.Service.Obs
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

            if (context.HostingEnvironment.IsDevelopment())
            {
                services
                   .AddSingleton<IObsController, FakeObsController>();
            }
            else
            {
                services
                    .AddSingleton<OBSWebsocket>()
                    .AddSingleton<IObsController, ObsController>();
            }

            services.AddHostedService<ObsService>();
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
