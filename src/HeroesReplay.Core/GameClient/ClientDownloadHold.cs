using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.GameClient;

/// <summary>Where a launch on a build that is not installed stands.</summary>
public enum BuildDownloadState
{
    /// <summary>The replay's build is installed, or the replay was not handed to HeroesSwitcher.</summary>
    NotDownloading,

    /// <summary>Blizzard is still fetching the build: its exe is not in <c>Versions\Base*</c> yet.</summary>
    Waiting,

    /// <summary>The build's exe is in <c>Versions\Base*</c>. The previous-patch rules take over.</summary>
    Arrived,

    /// <summary>The exe did not appear within <c>Spectate:BuildDownloadLimit</c>.</summary>
    Failed,
}

/// <summary>
/// A replay on a build that is not installed opens through HeroesSwitcher, and Blizzard
/// downloads that old client in the background (proven live on 2026-10-07: Base98285 arrived
/// about 40 s after the open). A build Blizzard no longer serves never arrives, or ends on the
/// version-mismatch dialog. That build is remembered in <c>Data\client-download-holds.json</c>
/// for <c>Spectate:BuildDownloadHold</c>, so the spectate queue, the launch route, and the
/// download role all treat it as not installed until the hold ends, instead of trying the
/// download again on every replay. The file is shared by the processes and survives a restart.
/// </summary>
public static class ClientDownloadHold
{
    public const string FileName = "client-download-holds.json";

    /// <summary>The default for <c>Spectate:BuildDownloadHold</c>.</summary>
    public static readonly TimeSpan DefaultHold = TimeSpan.FromHours(4);

    /// <summary>The default for <c>Spectate:BuildDownloadLimit</c>.</summary>
    public static readonly TimeSpan DefaultDownloadLimit = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static TimeSpan Hold(TimeSpan configured) =>
        configured > TimeSpan.Zero ? configured : DefaultHold;

    public static TimeSpan DownloadLimit(TimeSpan configured) =>
        configured > TimeSpan.Zero ? configured : DefaultDownloadLimit;

    public static string FilePath(string dataDirectory) =>
        string.IsNullOrWhiteSpace(dataDirectory)
            ? null
            : Path.Combine(dataDirectory.Trim(), FileName);

    /// <summary>
    /// The launch step on a build that is not installed. <paramref name="handedToSwitcher"/>
    /// is false until the replay was opened through HeroesSwitcher.
    /// </summary>
    public static BuildDownloadState Check(
        ReplayClientPatch patch,
        bool handedToSwitcher,
        bool exeExists,
        TimeSpan waited,
        TimeSpan limit
    )
    {
        if (patch != ReplayClientPatch.Download || !handedToSwitcher)
        {
            return BuildDownloadState.NotDownloading;
        }

        if (exeExists)
        {
            return BuildDownloadState.Arrived;
        }

        return waited >= DownloadLimit(limit)
            ? BuildDownloadState.Failed
            : BuildDownloadState.Waiting;
    }

    /// <summary>
    /// A version-mismatch dialog while the build's exe has not arrived is Blizzard refusing that
    /// build. Once the exe is there, the dialog is the ordinary mismatch hold.
    /// </summary>
    public static bool DialogFailsDownload(BuildDownloadState state, ClientHoldReason hold) =>
        state == BuildDownloadState.Waiting && hold == ClientHoldReason.VersionMismatch;

    /// <summary>The builds whose failed download is newer than <paramref name="hold"/>.</summary>
    public static IReadOnlyList<string> Active(
        IReadOnlyDictionary<string, DateTimeOffset> failures,
        DateTimeOffset now,
        TimeSpan hold
    )
    {
        var held = new List<string>();
        if (failures == null)
        {
            return held;
        }

        TimeSpan window = Hold(hold);
        foreach (KeyValuePair<string, DateTimeOffset> failure in failures)
        {
            string version = ReplayClientRoute.Normalize(failure.Key);
            if (version.Length > 0 && failure.Value <= now && now - failure.Value < window)
            {
                held.Add(version);
            }
        }

        return held;
    }

    /// <summary>The held builds recorded under <paramref name="dataDirectory"/>.</summary>
    public static IReadOnlyList<string> ActiveIn(
        string dataDirectory,
        DateTimeOffset now,
        TimeSpan hold
    ) => Active(Read(FilePath(dataDirectory)), now, hold);

    public static IReadOnlyDictionary<string, DateTimeOffset> Read(string path)
    {
        var failures = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        string json = DurableFile.ReadOrAside(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return failures;
        }

        try
        {
            Dictionary<string, DateTimeOffset> read = JsonSerializer.Deserialize<
                Dictionary<string, DateTimeOffset>
            >(json);
            if (read != null)
            {
                foreach (KeyValuePair<string, DateTimeOffset> entry in read)
                {
                    string version = ReplayClientRoute.Normalize(entry.Key);
                    if (version.Length > 0)
                    {
                        failures[version] = entry.Value;
                    }
                }
            }
        }
        catch (JsonException)
        {
            DurableFile.Aside(path);
        }

        return failures;
    }

    /// <summary>
    /// Holds <paramref name="version"/> from <paramref name="failedAt"/>. Holds that already
    /// ended are dropped, so the file stays small.
    /// </summary>
    public static void Record(string path, string version, DateTimeOffset failedAt, TimeSpan hold)
    {
        string build = ReplayClientRoute.Normalize(version);
        if (string.IsNullOrWhiteSpace(path) || build.Length == 0)
        {
            return;
        }

        TimeSpan window = Hold(hold);
        var kept = new SortedDictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, DateTimeOffset> entry in Read(path))
        {
            if (failedAt - entry.Value < window)
            {
                kept[entry.Key] = entry.Value;
            }
        }

        kept[build] = failedAt;
        DurableFile.Replace(path, JsonSerializer.Serialize(kept, Json));
    }
}
