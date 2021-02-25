using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Analysis
{
    public interface IAbilityDetector
    {
        bool IsAbility(Replay replay, GameEvent gameEvent, AbilityDetection abilityDetection);
    }
}