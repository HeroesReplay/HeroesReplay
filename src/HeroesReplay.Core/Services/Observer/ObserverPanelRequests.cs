using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// Chat requests and the spectator share this file. Twitch and the game are different processes.
/// </summary>
public sealed class ObserverPanelRequests : IObserverPanelRequests
{
    private static readonly Mutex ProcessLock = new(false, @"Local\HeroesReplay.PanelRequests");
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AppSettings settings;
    private readonly string filePath;

    public ObserverPanelRequests(AppSettings settings)
        : this(settings, DefaultPath()) { }

    public ObserverPanelRequests(AppSettings settings, string filePath)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public static string DefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "panel-requests.json"
        );

    public TimeSpan ShowDuration =>
        settings.Spectate.StatsPanelShowDuration <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(10)
            : settings.Spectate.StatsPanelShowDuration;

    public TimeSpan Cooldown =>
        settings.Spectate.StatsPanelCooldown <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(2)
            : settings.Spectate.StatsPanelCooldown;

    public ObserverPanelTryResult TryRequest(Panel panel, string username)
    {
        return WithState(state =>
        {
            if (Same(state.Showing, panel))
            {
                return new ObserverPanelTryResult(
                    ObserverPanelTryStatus.AlreadyVisible,
                    null,
                    username,
                    panel
                );
            }

            TimeSpan? remaining = Remaining(state, panel);
            if (remaining.HasValue)
            {
                return new ObserverPanelTryResult(
                    ObserverPanelTryStatus.Cooldown,
                    remaining,
                    username,
                    panel
                );
            }

            state.PendingPanel = panel.ToString();
            state.PendingUser = username;
            return new ObserverPanelTryResult(
                ObserverPanelTryStatus.Accepted,
                null,
                username,
                panel
            );
        });
    }

    public bool TryConsume(out Panel panel, out string requestedBy)
    {
        Panel consumed = Panel.None;
        string user = null;
        bool found = WithState(state =>
        {
            if (!TryParse(state.PendingPanel, out Panel pending))
            {
                return false;
            }

            consumed = pending;
            user = state.PendingUser;
            state.PendingPanel = null;
            state.PendingUser = null;
            state.Showing = pending.ToString();
            state.LastShownUtc[pending.ToString()] = DateTimeOffset.UtcNow;
            return true;
        });
        panel = consumed;
        requestedBy = user;
        return found;
    }

    public void MarkHidden(Panel panel)
    {
        WithState(state =>
        {
            if (Same(state.Showing, panel))
            {
                state.Showing = null;
            }

            return 0;
        });
    }

    private T WithState<T>(Func<StoredPanels, T> change)
    {
        Acquire();
        try
        {
            StoredPanels state = Read();
            T result = change(state);
            Write(state);
            return result;
        }
        finally
        {
            Release();
        }
    }

    private StoredPanels Read()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new StoredPanels();
            }

            return JsonSerializer.Deserialize<StoredPanels>(File.ReadAllText(filePath), Json)
                ?? new StoredPanels();
        }
        catch (JsonException)
        {
            return new StoredPanels();
        }
        catch (IOException)
        {
            return new StoredPanels();
        }
    }

    private void Write(StoredPanels state)
    {
        string directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
        File.Move(temp, filePath, overwrite: true);
    }

    private TimeSpan? Remaining(StoredPanels state, Panel panel)
    {
        if (
            state.LastShownUtc == null
            || !state.LastShownUtc.TryGetValue(panel.ToString(), out DateTimeOffset shown)
        )
        {
            return null;
        }

        TimeSpan elapsed = DateTimeOffset.UtcNow - shown;
        if (elapsed >= Cooldown)
        {
            return null;
        }

        return Cooldown - elapsed;
    }

    private static bool Same(string stored, Panel panel) =>
        TryParse(stored, out Panel parsed) && parsed == panel;

    private static bool TryParse(string value, out Panel panel)
    {
        if (Enum.TryParse(value, out panel) && panel != Panel.None)
        {
            return true;
        }

        panel = Panel.None;
        return false;
    }

    private static void Acquire()
    {
        try
        {
            if (!ProcessLock.WaitOne(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Could not lock panel-requests.json.");
            }
        }
        catch (AbandonedMutexException) { }
    }

    private static void Release()
    {
        try
        {
            ProcessLock.ReleaseMutex();
        }
        catch (ApplicationException) { }
    }

    private sealed class StoredPanels
    {
        public string PendingPanel { get; set; }
        public string PendingUser { get; set; }
        public string Showing { get; set; }
        public Dictionary<string, DateTimeOffset> LastShownUtc { get; set; } = new();
    }
}
