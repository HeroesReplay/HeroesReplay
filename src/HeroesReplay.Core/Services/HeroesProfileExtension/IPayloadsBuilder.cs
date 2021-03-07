using Heroes.ReplayParser;

namespace HeroesReplay.Core.Services.HeroesProfileExtension
{
    public interface IPayloadsBuilder
    {
        TalentPayloads CreatePayloads(Replay replay);
    }
}