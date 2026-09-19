using System;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class EmotingCalculator : IFocusCalculator
{
    private readonly AppSettings settings;
    private readonly IAbilityDetector abilityDetector;

    public EmotingCalculator(AppSettings settings, IAbilityDetector abilityDetector)
    {
        this.settings = settings;
        this.abilityDetector = abilityDetector;
    }

    public void Contribute(ReplayTimeline timeline)
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        Replay replay = timeline.Replay;

        for (int second = 0; second < timeline.TotalSeconds; second++)
        {
            var commands = timeline
                .GameEventsAt(second)
                .Where(e => e.eventType == GameEventType.CCmdEvent)
                .ToList();
            if (commands.Count == 0)
            {
                continue;
            }

            OfferAbility(
                timeline,
                replay,
                commands,
                second,
                settings.AbilityDetection.Hearth,
                settings.Weights.BStep,
                "bstepping",
                requireRepeat: true
            );
            OfferAbility(
                timeline,
                replay,
                commands,
                second,
                settings.AbilityDetection.Taunt,
                settings.Weights.Taunt,
                "taunting",
                requireRepeat: false
            );
            OfferAbility(
                timeline,
                replay,
                commands,
                second,
                settings.AbilityDetection.Dance,
                settings.Weights.Dance,
                "dancing",
                requireRepeat: false
            );
        }
    }

    private void OfferAbility(
        ReplayTimeline timeline,
        Replay replay,
        System.Collections.Generic.List<GameEvent> commands,
        int second,
        Models.AbilityDetection detection,
        float weight,
        string verb,
        bool requireRepeat
    )
    {
        foreach (
            IGrouping<Player, GameEvent> events in commands
                .Where(e => abilityDetector.IsAbility(replay, e, detection))
                .GroupBy(e => e.player)
        )
        {
            if (events.Key == null)
            {
                continue;
            }

            if (requireRepeat && events.Count() <= 3)
            {
                continue;
            }

            Unit heroUnit = null;
            foreach (
                Unit unit in events.Key.HeroUnits ?? new System.Collections.Generic.List<Unit>()
            )
            {
                if (timeline.TryGetPoint(unit, second, out _))
                {
                    heroUnit = unit;
                    break;
                }
            }

            if (heroUnit == null)
            {
                continue;
            }

            timeline.Offer(
                TimeSpan.FromSeconds(second),
                GetType(),
                heroUnit,
                events.Key,
                weight,
                $"{events.Key.Character} {verb}"
            );
        }
    }
}
