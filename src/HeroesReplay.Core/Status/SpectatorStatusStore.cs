using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Status;

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

        PatchIfChanged(status =>
        {
            update(status);
            return true;
        });
    }

    /// <summary>
    /// Like <see cref="Patch"/>, but status.json is written only when <paramref name="update"/>
    /// returns true, which is how it says it changed a field. A patch that changes nothing
    /// writes nothing (#357). True when the file was written.
    /// </summary>
    public bool PatchIfChanged(Func<SpectatorStatus, bool> update)
    {
        if (update == null)
        {
            throw new ArgumentNullException(nameof(update));
        }

        lock (gate)
        {
            if (!update(current))
            {
                return false;
            }

            current.UpdatedAt = DateTimeOffset.UtcNow;
            current.SnapshotStale = false;
            WriteUnlocked(current);
            return true;
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
        string json = JsonSerializer.Serialize(status, JsonOptions);
        // A watcher can hold status.json open. Replace retries instead of failing the focus loop.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                DurableFile.Replace(FilePath, json);
                return;
            }
            catch (Exception ex) when (IsSharingViolation(ex) && attempt < 60)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static bool IsSharingViolation(Exception ex) =>
        ex is UnauthorizedAccessException or IOException;
}
