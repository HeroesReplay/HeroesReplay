using System;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Providers;

/// <summary>
/// A requested replay keeps its redemption beside the file. The cache reads that file after a restart.
/// </summary>
public static class CachedRequestReward
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string PathFor(string replayPath)
    {
        if (string.IsNullOrWhiteSpace(replayPath))
        {
            return null;
        }

        return Path.ChangeExtension(replayPath, ".request.json");
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

    public static RewardQueueItem Read(string replayPath)
    {
        string path = PathFor(replayPath);
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
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }
}
