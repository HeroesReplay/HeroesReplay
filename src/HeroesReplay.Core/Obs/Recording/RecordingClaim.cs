using System;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Obs.Recording;

/// <summary>
/// The OBS recording a spectate process started and has not yet seen finalized (#318).
/// <c>services stop</c> reads it after every role exited, so a recording that a killed or
/// stuck spectate left running is stopped instead of growing until the disk is full.
/// </summary>
public sealed record RecordingClaim
{
    public int? ReplayId { get; init; }

    /// <summary>When OBS confirmed the recording active, UTC.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>The spectate process that owns the recording.</summary>
    public int ProcessId { get; init; }
}

/// <summary>
/// <c>%LOCALAPPDATA%\HeroesReplay\obs-recording.json</c>. Written when spectate takes ownership
/// of a recording, deleted when OBS finalizes it. A failed or timed-out stop keeps it.
/// </summary>
public sealed class RecordingClaimStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public RecordingClaimStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A claim path is required.", nameof(path));
        }

        FilePath = path;
    }

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "obs-recording.json"
        );

    public string FilePath { get; }

    public void Save(RecordingClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        DurableFile.Replace(FilePath, JsonSerializer.Serialize(claim, JsonOptions));
    }

    /// <summary>Null when there is no claim. An unreadable file is moved aside.</summary>
    public RecordingClaim TryLoad()
    {
        string json = DurableFile.ReadOrAside(FilePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RecordingClaim>(json, JsonOptions);
        }
        catch (JsonException)
        {
            DurableFile.Aside(FilePath);
            return null;
        }
    }

    public void Clear()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }
}
