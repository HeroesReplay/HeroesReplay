using System;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;
using HeroesReplay.Core.Services.Data;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class CampCaptureCalculator : IFocusCalculator
{
    private readonly AppSettings settings;
    private readonly IGameData gameData;

    public CampCaptureCalculator(AppSettings settings, IGameData gameData)
    {
        this.settings = settings;
        this.gameData = gameData;
    }

    public void Contribute(ReplayTimeline timeline)
    {
        CampCapture.Contribute(timeline, settings, gameData, bossesOnly: false);
    }
}

public class BossCampCaptureCalculator : IFocusCalculator
{
    private readonly AppSettings settings;
    private readonly IGameData gameData;

    public BossCampCaptureCalculator(AppSettings settings, IGameData gameData)
    {
        this.settings = settings;
        this.gameData = gameData;
    }

    public void Contribute(ReplayTimeline timeline)
    {
        CampCapture.Contribute(timeline, settings, gameData, bossesOnly: true);
    }
}

internal static class CampCapture
{
    public static void Contribute(
        ReplayTimeline timeline,
        AppSettings settings,
        IGameData gameData,
        bool bossesOnly
    )
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        for (int second = 0; second < timeline.TotalSeconds; second++)
        {
            foreach (TrackerEvent capture in timeline.TrackerEventsAt(second))
            {
                if (capture.TrackerEventType != ReplayTrackerEvents.TrackerEventType.StatGameEvent)
                {
                    continue;
                }

                if (capture.Data.dictionary[0].blobText != settings.TrackerEvents.JungleCampCapture)
                {
                    continue;
                }

                int teamId =
                    (int)
                        capture
                            .Data.dictionary[3]
                            .optionalData.array[0]
                            .dictionary[1]
                            .vInt.GetValueOrDefault() - 1;
                TimeSpan windowStart = capture.TimeSpan.Subtract(TimeSpan.FromSeconds(10));

                foreach (Unit unit in timeline.Replay.Units)
                {
                    if (
                        !unit.TimeSpanDied.HasValue
                        || unit.PlayerKilledBy == null
                        || unit.PlayerKilledBy.Team != teamId
                    )
                    {
                        continue;
                    }

                    if (
                        unit.TimeSpanBorn >= capture.TimeSpan
                        || unit.TimeSpanDied.Value >= capture.TimeSpan
                        || unit.TimeSpanDied.Value <= windowStart
                    )
                    {
                        continue;
                    }

                    bool isBoss = gameData.BossUnits.Contains(unit.Name);
                    if (isBoss != bossesOnly)
                    {
                        continue;
                    }

                    float weight = bossesOnly
                        ? settings.Weights.BossCapture
                        : settings.Weights.CampCapture;
                    string kind = bossesOnly ? "Boss" : "Camp";
                    timeline.Offer(
                        capture.TimeSpan,
                        bossesOnly
                            ? typeof(BossCampCaptureCalculator)
                            : typeof(CampCaptureCalculator),
                        unit,
                        unit.PlayerKilledBy,
                        weight,
                        $"{unit.PlayerKilledBy.Character} captured {unit.Name} ({kind}Captures)"
                    );
                }
            }
        }
    }
}
