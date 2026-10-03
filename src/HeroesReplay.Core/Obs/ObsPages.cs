using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Twitch.Predictions;
using HeroesReplay.Core.Twitch.Rewards;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs;

public enum ObsPageOutcome
{
    /// <summary>The page was rendered by this build.</summary>
    Written,

    /// <summary>There was nothing to render it from, so the page was left as it is.</summary>
    Kept,

    /// <summary>The page could not be rendered and was left as it is.</summary>
    Failed,
}

public sealed record ObsPageResult(string FileName, ObsPageOutcome Outcome, string Detail);

/// <summary>
/// The report-scene pages the application writes into <c>Location:DataDirectory</c>: the request
/// queue board and the prediction report. The running roles write them when the queue changes or
/// a prediction opens or resolves, so after an update they still show the last build's layout until
/// then. <see cref="Write"/> renders both again with this build from the saved queue and report,
/// without OBS or Twitch. A browser source reloads its page only when its scene is shown, so
/// <see cref="Reload"/> then reloads every browser source that shows a written page.
/// </summary>
public static class ObsPages
{
    public const string BrowserSourceKind = "browser_source";

    /// <summary>
    /// Reloads each OBS browser source whose local file or <c>file://</c> URL is one of
    /// <paramref name="fileNames"/> in <paramref name="dataDirectory"/>, and returns their names.
    /// Sources that show anything else are not touched.
    /// </summary>
    public static IReadOnlyList<string> Reload(
        IObsPageSession obs,
        string dataDirectory,
        IEnumerable<string> fileNames
    )
    {
        var pages = new HashSet<string>(
            fileNames.Select(name => Path.GetFullPath(Path.Combine(dataDirectory, name))),
            StringComparer.OrdinalIgnoreCase
        );
        var reloaded = new List<string>();
        if (pages.Count == 0)
        {
            return reloaded;
        }

        foreach (JObject input in obs.Get("GetInputList")["inputs"]?.OfType<JObject>() ?? [])
        {
            string name = (string)input["inputName"];
            if (
                name == null
                || !string.Equals(
                    (string)input["unversionedInputKind"],
                    BrowserSourceKind,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            JObject settings =
                obs.Get("GetInputSettings", new JObject { ["inputName"] = name })["inputSettings"]
                as JObject;
            if (ShowsPage(settings, pages))
            {
                obs.Reload(name);
                reloaded.Add(name);
            }
        }

        return reloaded;
    }

    private static bool ShowsPage(JObject settings, HashSet<string> pages)
    {
        if (
            (bool?)settings?["is_local_file"] == true
            && IsPage((string)settings["local_file"], pages)
        )
        {
            return true;
        }

        return Uri.TryCreate((string)settings?["url"], UriKind.Absolute, out Uri url)
            && url.IsFile
            && IsPage(url.LocalPath, pages);
    }

    private static bool IsPage(string path, HashSet<string> pages)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return pages.Contains(Path.GetFullPath(path));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    public static IReadOnlyList<ObsPageResult> Write(AppSettings settings)
    {
        string data = settings?.Location?.DataDirectory;
        if (string.IsNullOrWhiteSpace(data))
        {
            throw new InvalidOperationException("Location:DataDirectory is not set.");
        }

        return new[] { WriteQueue(settings, data), WritePrediction(data) };
    }

    private static ObsPageResult WriteQueue(AppSettings settings, string data)
    {
        string queueFileName = settings.Twitch?.QueueFileName;
        if (string.IsNullOrWhiteSpace(queueFileName))
        {
            return new ObsPageResult(
                QueueBoard.FileName,
                ObsPageOutcome.Failed,
                "Twitch:QueueFileName is not set."
            );
        }

        IReadOnlyList<RewardQueueItem> items = RequestQueue.Snapshot(
            Path.Combine(data, queueFileName)
        );
        if (items == null)
        {
            return new ObsPageResult(
                QueueBoard.FileName,
                ObsPageOutcome.Failed,
                $"{queueFileName} could not be read."
            );
        }

        try
        {
            var rewards = new SupportedRewardsHolder(
                new GameData(NullLogger<GameData>.Instance, settings)
            );
            QueueBoard.Write(Path.Combine(data, QueueBoard.FileName), items, rewards.Rewards);
            return new ObsPageResult(
                QueueBoard.FileName,
                ObsPageOutcome.Written,
                items.Count == 1 ? "1 request waiting." : $"{items.Count} requests waiting."
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ObsPageResult(QueueBoard.FileName, ObsPageOutcome.Failed, e.Message);
        }
    }

    private static ObsPageResult WritePrediction(string data)
    {
        try
        {
            return PredictionReportWriter.TryRewrite(data)
                ? new ObsPageResult(
                    PredictionReportWriter.ReportFileName,
                    ObsPageOutcome.Written,
                    $"From {PredictionReportWriter.SavedReportFileName}."
                )
                : new ObsPageResult(
                    PredictionReportWriter.ReportFileName,
                    ObsPageOutcome.Kept,
                    $"No {PredictionReportWriter.SavedReportFileName} yet. The next prediction writes both."
                );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ObsPageResult(
                PredictionReportWriter.ReportFileName,
                ObsPageOutcome.Failed,
                e.Message
            );
        }
    }
}
