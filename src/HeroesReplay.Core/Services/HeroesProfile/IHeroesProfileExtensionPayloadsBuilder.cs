using Heroes.ReplayParser;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public interface IHeroesProfileExtensionPayloadsBuilder
    {
        TalentExtensionPayloads CreatePayloads(Replay replay);
    }
}