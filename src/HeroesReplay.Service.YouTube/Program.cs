using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Service.YouTube.Core;

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

            if (context.HostingEnvironment.IsDevelopment())
            {
                services
                    .AddSingleton<IYouTubeUploader, FakeYouTubeUploader>();
            }
            else if (context.HostingEnvironment.IsProduction())
            {
                services
                    .AddSingleton<IYouTubeUploader, YouTubeUploader>();
            }

            services.AddHostedService<YouTubeService>();
        }

        private static void ConfigureAppConfig(HostBuilderContext context, IConfigurationBuilder builder)
        {
            builder.AddJsonFile("appsettings.Secrets.json", optional: true);
        }
    }
}
