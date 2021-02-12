
using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class HeroesProfileTwitchPayload
    {
        public HeroesProfileTwitchExtensionStep Step { get; init; }
        public List<Dictionary<string, string>> Content { get; init; }

        public HeroesProfileTwitchPayload SetGameSessionReplayId(string key, string replayId)
        {
            foreach (var dictionary in Content)
            {
                if (dictionary.ContainsKey(key))
                    dictionary[key] = replayId;
            }

            return this;
        }
    }
}