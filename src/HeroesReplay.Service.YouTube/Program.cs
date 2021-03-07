using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Twitch;
using HeroesReplay.Core.Services.YouTube;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using TwitchLib.Client;
using TwitchLib.Client.Interfaces;

namespace HeroesReplay.Service.YouTube
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

        private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            services.AddOptions();
            services.Configure<AppSettings>(context.Configuration);
            services.PostConfigure<AppSettings>(PostConfigure);

            if (context.HostingEnvironment.IsDevelopment())
            {
                services
                    .AddSingleton<ITwitchClient, FakeTwitchClient>()
                    .AddSingleton<IYouTubeUploader, FakeYouTubeUploader>();
            }
            else if (context.HostingEnvironment.IsProduction())
            {
                services
                    .AddSingleton<ITwitchClient, TwitchClient>()
                    .AddSingleton<IYouTubeUploader, YouTubeUploader>();
            }

            services.AddHostedService<YouTubeService>();
        }

        private static void PostConfigure(AppSettings appSettings)
        {
            appSettings.CurrentDirectory = Directory.GetCurrentDirectory();
            appSettings.ContextsDirectory = Path.Combine(appSettings.Location.DataDirectory, "Contexts");
        }

        private static void ConfigureAppConfig(HostBuilderContext context, IConfigurationBuilder builder)
        {
            builder.AddJsonFile("appsettings.Secrets.json", optional: true);
        }
    }
}
