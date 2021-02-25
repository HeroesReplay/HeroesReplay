using Heroes.ReplayParser;

namespace HeroesReplay.Core.Services.HeroesProfileExtension
{
    public interface IExtensionPayloadsBuilder
    {
        TalentPayloads CreatePayloads(Replay replay);
    }
}