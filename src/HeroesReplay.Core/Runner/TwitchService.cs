using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Clips.CreateClip;
using TwitchLib.Api.Helix.Models.Clips.GetClip;
using TwitchLib.Client;

namespace HeroesReplay.Core.Runner
{
    public class TwitchService : IDisposable
    {
        private readonly Settings settings;
        private readonly ILogger<TwitchService> logger;
        private readonly GameDataService gameDataService;
        private readonly TwitchAPI twitchAPI;
        private readonly TwitchClient twitchClient;
        private readonly HeroesProfileService heroesProfile;

        public TwitchService(
            ILogger<TwitchService> logger,
            IOptions<Settings> settings,
            GameDataService gameDataService,
            TwitchAPI twitchAPI,
            TwitchClient twitchClient,
            HeroesProfileService heroesProfileService)
        {
            this.settings = settings.Value;
            this.logger = logger;
            this.gameDataService = gameDataService;
            this.twitchAPI = twitchAPI;
            this.twitchClient = twitchClient;
            this.heroesProfile = heroesProfileService;
        }

        public void Connect()
        {
            this.twitchClient.Connect();
        }

        public async Task<Uri> SaveClip(StormPlayer stormPlayer, StormReplay stormReplay)
        {
            // Limited api functionality
            // Cannot modify the title of the clip
            // Cannot modify the time of the clip from 30 to 60 seconds
            // When twitch allow it, modify the title to a format like: [Hero] [Map] - [Quintuple, Quad, Tripple] kill
            CreatedClipResponse response = await twitchAPI.Helix.Clips.CreateClipAsync(settings.TwitchBroadcasterId);
            GetClipResponse clip = await twitchAPI.Helix.Clips.GetClipAsync(response.CreatedClips[0].Id);

            return new Uri(clip.Clips[0].Url);
        }

        public async void SendClipToChat()
        {

        }

        public void Disconnect()
        {
            this.twitchClient.Disconnect();
        }

        public void RegisterEvents()
        {
            this.twitchClient.OnChatCommandReceived += OnChatCommandReceived;
        }

        private void OnChatCommandReceived(object? sender, TwitchLib.Client.Events.OnChatCommandReceivedArgs e)
        {
            if (e.Command.CommandText.Equals("match"))
            {
                // heroesProfile.GetMatchLink()
            }
        }

        public void UnregisterEvents()
        {
            this.twitchClient.OnChatCommandReceived -= OnChatCommandReceived;
        }

        public void Dispose()
        {
            this.twitchClient.Disconnect();
        }
    }
}
