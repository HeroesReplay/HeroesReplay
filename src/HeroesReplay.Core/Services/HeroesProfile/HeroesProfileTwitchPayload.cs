
using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class HeroesProfileTwitchPayload
    {
        public HeroesProfileTwitchExtensionStep Step { get; init; }
        public List<Dictionary<string, string>> Content { get; init; }

        public HeroesProfileTwitchPayload SetGameSessionReplayId(string replayId)
        {
            foreach (var dictionary in Content)
            {
                if (dictionary.ContainsKey(TwitchExtensionFormKeys.SessionId))
                    dictionary[TwitchExtensionFormKeys.SessionId] = replayId;
            }

            return this;
        }
    }
}