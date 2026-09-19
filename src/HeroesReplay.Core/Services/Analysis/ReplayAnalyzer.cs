using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.HeroesProfileExtension;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Analysis;

public class ReplayAnalyzer : IReplayAnalyzer
{
    private readonly IEnumerable<IFocusCalculator> calculators;
    private readonly IGameData gameData;
    private readonly ILogger<ReplayAnalyzer> logger;
    private readonly AppSettings settings;
    private readonly IExtensionPayloadsBuilder payloadsBuilder;

    public ReplayAnalyzer(
        ILogger<ReplayAnalyzer> logger,
        AppSettings settings,
        IExtensionPayloadsBuilder payloadsBuilder,
        IEnumerable<IFocusCalculator> calculators,
        IGameData gameData
    )
    {
        this.calculators = calculators ?? throw new ArgumentNullException(nameof(calculators));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.payloadsBuilder = payloadsBuilder;
        this.gameData = gameData;
    }

    public TimeSpan GetEnd(Replay replay)
    {
        if (replay == null)
        {
            throw new ArgumentNullException(nameof(replay));
        }

        if (gameData == null)
        {
            return replay.ReplayLength;
        }

        return replay
            .Units.Where(unit =>
                gameData.CoreUnits.Contains(unit.Name) && unit.TimeSpanDied.HasValue
            )
            .Select(core => core.TimeSpanDied.GetValueOrDefault())
            .DefaultIfEmpty(replay.ReplayLength)
            .Min();
    }

    public TimeSpan GetSessionEnd(Replay replay)
    {
        if (replay == null)
        {
            throw new ArgumentNullException(nameof(replay));
        }

        if (replay.TrackerEvents == null || settings.TrackerEvents == null)
        {
            return GetEnd(replay);
        }

        TimeSpan? votes = null;
        if (settings.TrackerEvents.EndOfGameStatEvents != null)
        {
            foreach (string name in settings.TrackerEvents.EndOfGameStatEvents)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                TrackerEvent last = replay.TrackerEvents.LastOrDefault(e =>
                    e.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent
                    && e.Data?.dictionary != null
                    && e.Data.dictionary.TryGetValue(0, out TrackerEventStructure key)
                    && key.blobText == name
                );
                if (last != null && (votes == null || last.TimeSpan > votes))
                {
                    votes = last.TimeSpan;
                }
            }
        }

        if (votes.HasValue)
        {
            return votes.Value;
        }

        if (settings.TrackerEvents.UseScoreResultEvent)
        {
            TrackerEvent score = replay.TrackerEvents.LastOrDefault(e =>
                e.TrackerEventType == ReplayTrackerEvents.TrackerEventType.ScoreResultEvent
            );
            if (score != null)
            {
                return score.TimeSpan;
            }
        }

        return GetEnd(replay);
    }

    public IReadOnlyDictionary<TimeSpan, Panel> GetPanels(Replay replay)
    {
        if (replay == null)
        {
            throw new ArgumentNullException(nameof(replay));
        }

        IDictionary<TimeSpan, Panel> panels = new SortedDictionary<TimeSpan, Panel>();

        if (settings.ParseOptions.ShouldParseUnits)
        {
            foreach (
                var deathTime in replay
                    .Players.SelectMany(x => x.HeroUnits)
                    .Where(u => u.TimeSpanDied.HasValue)
                    .GroupBy(x => x.TimeSpanDied.GetValueOrDefault())
            )
            {
                panels[deathTime.Key] = Panel.KillsDeathsAssists;
            }
        }

        if (settings.ParseOptions.ShouldParseStatistics)
        {
            var padding = TimeSpan.FromSeconds(1);

            foreach (
                var talentTime in replay
                    .TeamLevels.SelectMany(x => x)
                    .Where(x => settings.Spectate.TalentLevels.Contains(x.Key))
                    .Select(x => x.Value)
            )
            {
                panels[talentTime.Subtract(padding)] = Panel.Talents;
                panels[talentTime] = Panel.Talents;
                panels[talentTime.Add(padding)] = Panel.Talents;
            }
        }

        return new ReadOnlyDictionary<TimeSpan, Panel>(panels);
    }

    public ITalentPayloads GetPayloads(Replay replay)
    {
        if (payloadsBuilder == null)
        {
            return null;
        }

        return payloadsBuilder.CreatePayloads(replay);
    }

    public IReadOnlyDictionary<TimeSpan, Focus> GetPlayers(Replay replay)
    {
        if (replay == null)
        {
            throw new ArgumentNullException(nameof(replay));
        }

        ReplayTimeline timeline = ReplayTimeline.Create(replay);
        foreach (IFocusCalculator calculator in calculators)
        {
            calculator.Contribute(timeline);
        }

        timeline.ApplyDeathContext(settings.Spectate);
        IReadOnlyDictionary<TimeSpan, Focus> result = timeline.ToDictionary();
        logger.LogInformation("focus count: {Count}", result.Count);
        return result;
    }

    public TimeSpan GetStart(Replay replay)
    {
        if (replay == null)
        {
            throw new ArgumentNullException(nameof(replay));
        }

        TrackerEvent gates = replay.TrackerEvents?.FirstOrDefault(x =>
            x.Data.dictionary[0].blobText == settings.TrackerEvents.GatesOpen
        );
        return gates?.TimeSpan ?? TimeSpan.Zero;
    }

    public bool GetIsCarriedObjective(Replay replay) =>
        replay != null
        && (
            settings.Maps.CarriedObjectives.Contains(replay.Map)
            || settings.Maps.CarriedObjectives.Contains(replay.MapAlternativeName)
        );

    public IReadOnlyDictionary<int, IReadOnlyCollection<string>> GetTeamBans(Replay replay)
    {
        return new Dictionary<int, IReadOnlyCollection<string>>
        {
            [0] = ResolveBans(replay.TeamHeroBans[0]),
            [1] = ResolveBans(replay.TeamHeroBans[1]),
        };
    }

    private IReadOnlyCollection<string> ResolveBans(IEnumerable<string> bans)
    {
        if (gameData == null || bans == null)
        {
            return Array.Empty<string>();
        }

        var resolved = new List<string>();
        foreach (string ban in bans)
        {
            if (string.IsNullOrWhiteSpace(ban))
            {
                continue;
            }

            foreach (var hero in gameData.Heroes)
            {
                if (
                    ban.Equals(hero.Name, StringComparison.OrdinalIgnoreCase)
                    || ban.Equals(hero.UnitId, StringComparison.OrdinalIgnoreCase)
                    || ban.Equals(hero.AttributeId, StringComparison.OrdinalIgnoreCase)
                    || ban.Equals(hero.HyperlinkId, StringComparison.OrdinalIgnoreCase)
                )
                {
                    resolved.Add(hero.HyperlinkId);
                    break;
                }
            }
        }

        return resolved;
    }
}
