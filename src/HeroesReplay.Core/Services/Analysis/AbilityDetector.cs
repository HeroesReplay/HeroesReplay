using System;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Analysis;

public class AbilityDetector : IAbilityDetector
{
    public bool IsAbility(Replay replay, GameEvent gameEvent, AbilityDetection abilityDetection)
    {
        if (abilityDetection == null || gameEvent == null || replay == null)
        {
            return false;
        }

        int abilityLink = GetAbilityLink(gameEvent.data);

        if (
            abilityDetection.CmdIndex.HasValue
            && abilityDetection.CmdIndex.Value != GetAbilityCmdIndex(gameEvent.data)
        )
        {
            return false;
        }

        if (abilityDetection.AbilityBuilds == null)
        {
            return false;
        }

        foreach (AbilityBuild abilityBuild in abilityDetection.AbilityBuilds)
        {
            if (abilityLink != abilityBuild.AbilityLink)
            {
                continue;
            }

            if (
                IsBuildInRange(
                    replay.ReplayBuild,
                    abilityBuild.GreaterEqualBuild,
                    abilityBuild.LessThanBuild
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsBuildInRange(int replayBuild, int? greaterEqualBuild, int? lessThanBuild)
    {
        if (greaterEqualBuild.HasValue && replayBuild < greaterEqualBuild.Value)
        {
            return false;
        }

        if (lessThanBuild.HasValue && replayBuild >= lessThanBuild.Value)
        {
            return false;
        }

        return true;
    }

    private static int GetAbilityLink(TrackerEventStructure structure) =>
        Convert.ToInt32(structure?.array[1]?.array[0]?.unsignedInt.GetValueOrDefault());

    private static int GetAbilityCmdIndex(TrackerEventStructure trackerEvent) =>
        Convert.ToInt32(trackerEvent.array[1]?.array[1]?.unsignedInt.GetValueOrDefault());
}
