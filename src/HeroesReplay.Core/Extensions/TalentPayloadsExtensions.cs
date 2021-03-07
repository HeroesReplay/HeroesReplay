using HeroesReplay.Core.Services.HeroesProfileExtension;

namespace HeroesReplay.Core.Extensions
{
    public static class TalentPayloadsExtensions
    {
        public static TalentsPayload SetGameSessionReplayId(this TalentsPayload payload, string replayId)
        {
            foreach (var dictionary in payload.Content)
            {
                if (dictionary.ContainsKey(FormKeys.SessionId))
                    dictionary[FormKeys.SessionId] = replayId;
            }

            return payload;
        }
    }
}
