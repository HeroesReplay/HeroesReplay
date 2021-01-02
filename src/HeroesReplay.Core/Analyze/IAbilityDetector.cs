using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;

using HeroesReplay.Core.Models;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core
{
    public interface IAbilityDetector
    {
        bool IsAbility(Replay replay, GameEvent gameEvent, AbilityDetection abilityDetection);
    }
}