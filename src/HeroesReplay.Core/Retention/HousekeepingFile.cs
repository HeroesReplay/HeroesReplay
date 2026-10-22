using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Retention;

/// <summary>The disk at the end of a pass, for <c>check storage</c>.</summary>
public sealed class StorageSummary
{
    public DateTimeOffset MeasuredAt { get; set; }
    public string DataDirectory { get; set; }
    public long DataBytes { get; set; }
    public long StateBytes { get; set; }
    public long? FreeBytes { get; set; }
    public string LargestCategory { get; set; }
    public long LargestBytes { get; set; }
}

/// <summary><c>%LOCALAPPDATA%\HeroesReplay\housekeeping.json</c>: the last pass and the disk after it.</summary>
public sealed class HousekeepingState
{
    public HousekeepingRun LastRun { get; set; }
    public StorageSummary Storage { get; set; }
}

/// <summary>Source-generated JSON for the housekeeping files (#454, #487).</summary>
[JsonSerializable(typeof(HousekeepingState))]
internal sealed partial class RetentionJsonContext : JsonSerializerContext;

public static class HousekeepingFile
{
    public const string FileName = "housekeeping.json";

    internal static readonly RetentionJsonContext Json = new(
        new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }
    );

    public static string PathIn(string stateDirectory) =>
        Path.Combine(stateDirectory ?? HousekeepingSettings.DefaultStateDirectory, FileName);

    public static void Save(string path, HousekeepingState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(state);
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json.HousekeepingState));
        File.Move(temp, path, overwrite: true);
    }

    public static HousekeepingState TryLoad(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) || !File.Exists(path)
                ? null
                : JsonSerializer.Deserialize(File.ReadAllText(path), Json.HousekeepingState);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
