using System;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Status;

public sealed class SpectatorStatusStore
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object gate = new();
    private SpectatorStatus current = Idle();

    public string FilePath { get; }

    public SpectatorStatusStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HeroesReplay",
                "status.json"
            )
        ) { }

    public SpectatorStatusStore(string filePath)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public static SpectatorStatus Idle() =>
        new()
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            SpectatorRunning = false,
            Phase = "Idle",
        };

    public void Patch(Action<SpectatorStatus> update)
    {
        if (update == null)
        {
            throw new ArgumentNullException(nameof(update));
        }

        lock (gate)
        {
            update(current);
            current.UpdatedAt = DateTimeOffset.UtcNow;
            current.SnapshotStale = false;
            WriteUnlocked(current);
        }
    }

    public void MarkIdle(string phase = "Idle")
    {
        Patch(status =>
        {
            status.SpectatorRunning = false;
            status.Phase = phase;
            status.ObsSession = false;
            status.Focus = null;
        });
    }

    public SpectatorStatus Read()
    {
        SpectatorStatus status = TryReadFile() ?? CopyCurrent();
        MarkStale(status);
        return status;
    }

    /// <summary>
    /// Reads the status file another process wrote. Returns null when the file is missing or busy
    /// so a watcher does not treat this process's idle memory as the spectator stopping.
    /// </summary>
    public SpectatorStatus TryReadShared()
    {
        SpectatorStatus status = TryReadFile();
        if (status == null)
        {
            return null;
        }

        MarkStale(status);
        return status;
    }

    private static void MarkStale(SpectatorStatus status)
    {
        if (DateTimeOffset.UtcNow - status.UpdatedAt > StaleAfter)
        {
            status.SnapshotStale = true;
            status.SpectatorRunning = false;
        }
    }

    public string ReadJson() => JsonSerializer.Serialize(Read(), JsonOptions);

    private SpectatorStatus CopyCurrent()
    {
        lock (gate)
        {
            return JsonSerializer.Deserialize<SpectatorStatus>(
                    JsonSerializer.Serialize(current, JsonOptions),
                    JsonOptions
                ) ?? Idle();
        }
    }

    private SpectatorStatus TryReadFile()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<SpectatorStatus>(json, JsonOptions);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void WriteUnlocked(SpectatorStatus status)
    {
        string directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(status, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }
}
