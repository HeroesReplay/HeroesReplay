using Heroes.ReplayParser;

namespace HeroesReplay.Core.Services.Analyzer
{
    public interface IPayloadsBuilder
    {
        TalentPayloads CreatePayloads(Replay replay);
    }
}