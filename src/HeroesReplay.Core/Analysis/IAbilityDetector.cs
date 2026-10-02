using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;

namespace HeroesReplay.Core.Analysis;

public interface IAbilityDetector
{
    bool IsAbility(Replay replay, GameEvent gameEvent, AbilityDetection abilityDetection);
}
