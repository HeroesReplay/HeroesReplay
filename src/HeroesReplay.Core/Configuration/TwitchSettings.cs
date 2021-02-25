using System;

namespace HeroesReplay.Core.Configuration
{
    public class TwitchSettings
    {
        public string Account { get; set; }
        public string AccessToken { get; set; }
        public string ClientId { get; set; }
        public string RefreshToken { get; set; }
        public string Channel { get; set; }

        public bool EnableRequests { get; set; }
        public bool EnableTwitchClips { get; set; }
        public bool EnablePubSub { get; set; }
        public bool EnableChatBot { get; set; }
        public bool DryRunMode { get; set; }

        public string RequestsFileName { get; set; }
        public Uri TokenRefreshUri { get; set; }
    }
}