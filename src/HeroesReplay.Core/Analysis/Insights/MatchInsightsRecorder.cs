using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Analysis.Insights;

/// <summary>Reads a spectated replay's insights at session end and keeps them next to the recording.</summary>
public interface IMatchInsightsRecorder
{
    /// <summary>
    /// The replay's insights, also written to <c>insights.json</c> in <paramref name="directory"/>
    /// when it is given. Null when they could not be read: the publication goes on without them.
    /// </summary>
    MatchInsights Record(
        Replay replay,
        IReadOnlyList<Hero> catalog,
        IReadOnlyDictionary<TimeSpan, Focus> focus,
        string directory
    );
}

/// <summary>
/// Runs once per replay when the session ends (#412), on the replay the spectator already parsed
/// and its focus timeline, so nothing is parsed or calculated twice. A failure is logged and never
/// stops the YouTube entry.
/// </summary>
public sealed class MatchInsightsRecorder : IMatchInsightsRecorder
{
    private readonly IMatchInsightsReader reader;
    private readonly ILogger<MatchInsightsRecorder> logger;

    public MatchInsightsRecorder(IMatchInsightsReader reader, ILogger<MatchInsightsRecorder> logger)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public MatchInsights Record(
        Replay replay,
        IReadOnlyList<Hero> catalog,
        IReadOnlyDictionary<TimeSpan, Focus> focus,
        string directory
    )
    {
        if (replay == null)
        {
            return null;
        }

        MatchInsights insights;
        try
        {
            insights = reader.Read(replay, catalog, focus);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            logger.LogWarning(
                e,
                "Could not read the match insights; the YouTube entry is written without them."
            );
            return null;
        }

        logger.LogInformation(
            "Match insights: solo laners {SoloLaners}, standouts {Standouts}, MVP {Mvp}, core race {CoreRace}, comeback {Comeback}, guide builds {Builds}, most followed {Followed}.",
            string.Join(
                ", ",
                insights.Lanes.Teams.Select(team =>
                    $"{team.SoloLaner?.Hero ?? "none"} ({team.Confidence})"
                )
            ),
            insights.Players.Standouts.Count,
            insights.Players.Mvp?.Hero ?? "none",
            insights.Finish.CoreRace,
            insights.Comeback.Comeback,
            insights.Builds.Players.Count,
            insights.Focus.Top?.Hero ?? "none"
        );

        if (!string.IsNullOrWhiteSpace(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    Path.Combine(directory, InsightsJson.FileName),
                    InsightsJson.Write(insights)
                );
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(
                    e,
                    "Could not write {File} in {Directory}.",
                    InsightsJson.FileName,
                    directory
                );
            }
        }

        return insights;
    }
}
