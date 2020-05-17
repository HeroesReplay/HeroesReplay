using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Runner
{
    public class TwitchService
    {
        private readonly Settings settings;
        private readonly ILogger<TwitchService> logger;

        public TwitchService(ILogger<TwitchService> logger, IOptions<Settings> settings)
        {
            this.settings = settings.Value;
            this.logger = logger;
        }

        public async Task<Uri> SaveClip()
        {
            var api = new TwitchLib.Api.TwitchAPI();
            api.Settings.AccessToken = settings.TwitchAccessToken;
            api.Settings.ClientId = settings.TwitchClientId;

            var response = await api.Helix.Clips.CreateClipAsync(settings.TwitchBroadcasterId);

            return await Task.FromResult(new Uri(""));
        }
    }
}
