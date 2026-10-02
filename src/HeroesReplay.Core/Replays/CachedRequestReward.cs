using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Requests;

namespace HeroesReplay.Core.Replays;

/// <summary>
/// A requested replay keeps its redemption beside the file. The downloader writes it before the
/// replay file appears, and the cache reads it after a restart. A replay loaded from any other
/// path (Data\Standard, a connectivity resume, the report preload) finds its request by replay id.
/// </summary>
public static class CachedRequestReward
{
    public const string Extension = ".request.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string PathFor(string replayPath)
    {
        if (string.IsNullOrWhiteSpace(replayPath))
        {
            return null;
        }

        return Path.ChangeExtension(replayPath, Extension);
    }

    public static void Write(string replayPath, RewardQueueItem item)
    {
        string path = PathFor(replayPath);
        if (path == null || item?.Request == null)
        {
            return;
        }

        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(item, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Removes the sidecar of a replay whose download did not finish.</summary>
    public static void Delete(string replayPath)
    {
        string path = PathFor(replayPath);
        if (path != null && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static RewardQueueItem Read(string replayPath)
    {
        return ReadSidecar(PathFor(replayPath));
    }

    /// <summary>
    /// The request for <paramref name="replayId"/> in the requests folder, whatever folder the
    /// replay file itself was loaded from. Null when that id was not requested.
    /// </summary>
    public static RewardQueueItem FindById(string requestsDirectory, int replayId)
    {
        if (
            replayId <= 0
            || string.IsNullOrWhiteSpace(requestsDirectory)
            || !Directory.Exists(requestsDirectory)
        )
        {
            return null;
        }

        string prefix = replayId.ToString(CultureInfo.InvariantCulture);
        string[] sidecars;
        try
        {
            sidecars = Directory.GetFiles(
                requestsDirectory,
                prefix + "*" + Extension,
                SearchOption.TopDirectoryOnly
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        Array.Sort(sidecars, StringComparer.OrdinalIgnoreCase);
        foreach (string sidecar in sidecars)
        {
            RewardQueueItem item = ReadSidecar(sidecar);
            if (RequestedId(item) == replayId)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// Links <paramref name="loaded"/> to its request when it has none yet. True when a request
    /// was attached.
    /// </summary>
    public static bool Attach(LoadedReplay loaded, string requestsDirectory)
    {
        if (loaded == null || loaded.RewardQueueItem?.Request != null)
        {
            return false;
        }

        int replayId = loaded.ReplayId ?? loaded.HeroesProfileReplay?.Id ?? 0;
        RewardQueueItem item =
            Read(loaded.FileInfo?.FullName) ?? FindById(requestsDirectory, replayId);
        if (item == null)
        {
            return false;
        }

        loaded.RewardQueueItem = item;
        loaded.HeroesProfileReplay ??= item.HeroesProfileReplay;
        return true;
    }

    private static int? RequestedId(RewardQueueItem item)
    {
        if (item?.HeroesProfileReplay?.Id is int profileId && profileId > 0)
        {
            return profileId;
        }

        return item?.Request?.ReplayId;
    }

    private static RewardQueueItem ReadSidecar(string path)
    {
        if (path == null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            RewardQueueItem item = JsonSerializer.Deserialize<RewardQueueItem>(
                File.ReadAllText(path)
            );
            return item?.Request == null ? null : item;
        }
        catch (Exception ex)
            when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
