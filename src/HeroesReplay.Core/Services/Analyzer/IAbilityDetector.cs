using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;

namespace HeroesReplay.Core.Services.Analyzer
{
    public interface IAbilityDetector
    {
        bool IsAbility(Replay replay, GameEvent gameEvent, AbilityDetection abilityDetection);
    }
}