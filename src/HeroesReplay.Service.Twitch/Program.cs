using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Queue;
using HeroesReplay.Core.Services.Twitch;
using HeroesReplay.Core.Services.Twitch.ChatMessages;
using HeroesReplay.Core.Services.Twitch.RedeemedRewards;
using HeroesReplay.Core.Services.Twitch.Rewards;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Polly.Caching;
using Polly.Caching.Memory;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using TwitchLib.Api;
using TwitchLib.Api.Core;
using TwitchLib.Api.Core.Interfaces;
using TwitchLib.Api.Interfaces;
using TwitchLib.Client;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;
using TwitchLib.PubSub;
using TwitchLib.PubSub.Interfaces;

namespace HeroesReplay.Service.Twitch
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

                    await host.RunAsync(cts.Token);
                }
            }
        }

        private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            services.AddOptions();
            services.Configure<AppSettings>(context.Configuration);

            IEnumerable<Type> types = AppDomain.CurrentDomain.GetAssemblies().SelectMany(assembly => assembly.GetTypes());

            foreach (var type in types.Where(type => type.IsClass && typeof(IRewardHandler).IsAssignableFrom(type)))
            {
                services.AddSingleton(typeof(IRewardHandler), type);
            }

            foreach (var type in types.Where(type => type.IsClass && typeof(IMessageHandler).IsAssignableFrom(type)))
            {
                services.AddSingleton(typeof(IMessageHandler), type);
            }

            if (context.HostingEnvironment.IsDevelopment())
            {
                services
                    .AddSingleton<ITwitchBot, FakeTwitchBot>()
                    .AddSingleton<ITwitchClient, FakeTwitchClient>();
            }
            else if (context.HostingEnvironment.IsProduction())
            {
                services
                    .AddSingleton(serviceProvider => new ConnectionCredentials("", ""))
                    .AddSingleton<IApiSettings>(serviceProvider => new ApiSettings { AccessToken = "", ClientId = "" })
                    .AddSingleton<ITwitchBot, TwitchBot>()
                    .AddSingleton<ITwitchPubSub, TwitchPubSub>()
                    .AddSingleton<ITwitchAPI, TwitchAPI>()
                    .AddSingleton<ITwitchClient, TwitchClient>();
            }

            services
                .AddMemoryCache()
                .AddSingleton<IAsyncCacheProvider, MemoryCacheProvider>()
                .AddSingleton<IHeroesProfileService, HeroesProfileService>()
                .AddHttpClient<HeroesProfileService>();

            services
                .AddSingleton<IGameData, GameData>()
                .AddSingleton<IRequestQueue, RequestQueue>()
                .AddSingleton<IRewardRequestFactory, RewardRequestFactory>()
                .AddSingleton<IOnRewardHandler, OnRewardRedeemedHandler>()
                .AddSingleton<IOnMessageHandler, OnMessageReceivedHandler>()
                .AddSingleton<ICustomRewardsHolder, SupportedRewardsHolder>()
                .AddHostedService<TwitchService>();
        }

        private static void ConfigureAppConfig(HostBuilderContext context, IConfigurationBuilder builder)
        {
            builder.AddJsonFile("appsettings.Secrets.json", optional: true);
        }
    }
}
