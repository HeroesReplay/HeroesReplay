
using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class ExtensionPayload
    {
        public ExtensionStep Step { get; init; }
        public List<Dictionary<string, string>> Content { get; init; }

        public ExtensionPayload SetGameSessionReplayId(string replayId)
        {
            foreach (var dictionary in Content)
            {
                if (dictionary.ContainsKey(ExtensionFormKeys.SessionId))
                    dictionary[ExtensionFormKeys.SessionId] = replayId;
            }

            return this;
        }
    }
}