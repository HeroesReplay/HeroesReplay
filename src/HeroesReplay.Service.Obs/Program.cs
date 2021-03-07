using System.IO;
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
                IHostBuilder builder = Host
                   .CreateDefaultBuilder()
                   .ConfigureServices(services => services.AddSingleton(cts))
                   .ConfigureServices(ConfigureServices)
                   .ConfigureAppConfiguration(ConfigureAppConfig);

                await builder.RunConsoleAsync(cts.Token);
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
            else if (context.HostingEnvironment.IsStaging())
            {
                services
                  .AddSingleton<OBSWebsocket>()
                  .AddSingleton<IObsController, ObsController>();
            }
            else if (context.HostingEnvironment.IsProduction())
            {
                services
                    .AddSingleton<OBSWebsocket>()
                    .AddSingleton<IObsController, ObsController>();
            }

            services.AddSingleton<IObsEntryMonitor, ObsEntryMonitor>();
            services.AddHostedService<ObsService>();
        }

        public static void PostConfigure(AppSettings appSettings)
        {
            appSettings.CurrentDirectory = Directory.GetCurrentDirectory();
            appSettings.ContextsDirectory = Path.Combine(appSettings.Location.DataDirectory, "Contexts");
        }
    }
}
