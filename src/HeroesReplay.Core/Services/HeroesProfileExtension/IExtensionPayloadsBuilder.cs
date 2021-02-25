using Heroes.ReplayParser;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public interface IExtensionPayloadsBuilder
    {
        TalentPayloads CreatePayloads(Replay replay);
    }
}