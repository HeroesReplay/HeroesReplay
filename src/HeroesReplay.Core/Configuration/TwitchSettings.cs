using System;

namespace HeroesReplay.Core.Configuration
{
    public record TwitchSettings
    {
        public string Account { get; init; }
        public string AccessToken { get; init; }
        public string ClientId { get; init; }
        public string RefreshToken { get; init; }
        public string Channel { get; init; }

        public bool EnableReplayRequests { get; init; }
        public bool EnableTwitchClips { get; init; }
        public bool EnablePubSub { get; init; }
        public bool EnableChatBot { get; init; }
        public bool DryRunMode { get; init; }

        public string RewardTitle { get; init; }

        public string ReplayRequestsFileName { get; init; }
        public Uri TokenRefreshUri { get; init; }
    }
}