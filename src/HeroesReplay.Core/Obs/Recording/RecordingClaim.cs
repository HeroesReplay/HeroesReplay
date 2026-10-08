using System;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Obs.Recording;

/// <summary>
/// The OBS recording a spectate process started and has not yet seen finalized (#318).
/// <c>services stop</c> reads it after every role exited, and the next spectate reads it when it
/// starts (#342), so a recording that a killed or stuck spectate left running is stopped instead
/// of growing until the disk is full.
/// </summary>
public sealed record RecordingClaim
{
    private static readonly Lazy<DateTimeOffset?> ThisProcessStartedAt = new(() =>
        ProcessTable.Find(Environment.ProcessId)?.StartTime
    );

    public int? ReplayId { get; init; }

    /// <summary>When OBS confirmed the recording active, UTC.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>The spectate process that owns the recording.</summary>
    public int ProcessId { get; init; }

    /// <summary>
    /// When <see cref="ProcessId"/> started (<see cref="ProcessTable"/>, UTC), so a reused pid is
    /// not taken for the claimant (#342). Null in a claim written before #342.
    /// </summary>
    public DateTimeOffset? ProcessStartedAt { get; init; }

    /// <summary>The claim of a recording this process asks OBS for at <paramref name="startedAt"/>.</summary>
    public static RecordingClaim ForThisProcess(int? replayId, DateTimeOffset startedAt) =>
        new()
        {
            ReplayId = replayId,
            StartedAt = startedAt,
            ProcessId = Environment.ProcessId,
            ProcessStartedAt = ThisProcessStartedAt.Value,
        };
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
