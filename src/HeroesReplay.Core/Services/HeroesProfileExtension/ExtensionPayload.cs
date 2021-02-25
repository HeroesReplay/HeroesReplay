using System.Collections.Generic;

namespace HeroesReplay.Core.Services.HeroesProfileExtension
{
    public class ExtensionPayload
    {
        public ExtensionStep Step { get; set; }
        public List<Dictionary<string, string>> Content { get; set; }

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