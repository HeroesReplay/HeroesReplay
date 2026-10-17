using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.HeroFocus.Targeted;

/// <summary>
/// <c>Data\Focus\targeted.json</c>: what the targeted fetch remembers between steps (#433). The
/// download role is its only writer. It keeps hero attribute ids and replay ids, never a player.
/// </summary>
public sealed class FocusTargetedState
{
    /// <summary>The fetched replays kept.</summary>
    public const int FetchedCap = 500;

    /// <summary>When the fetch first ran: a hero is not "unseen" before <c>After</c> has passed since.</summary>
    public DateTime? FirstStepAtUtc { get; set; }

    /// <summary>The last fetch attempt, for <c>Interval</c>.</summary>
    public DateTime? LastFetchAtUtc { get; set; }

    /// <summary>Hero attribute id to the last time a waiting replay had a human on it.</summary>
    public Dictionary<string, DateTime> Seen { get; set; } = new();

    /// <summary>Hero attribute id to the last time the fetch tried it.</summary>
    public Dictionary<string, DateTime> Tried { get; set; } = new();

    public List<FocusTargetedReplay> Fetched { get; set; } = new();

    /// <summary>Every Heroes Profile call the fetch made in the last week.</summary>
    public List<DateTime> Calls { get; set; } = new();

    public int CallsSince(DateTime sinceUtc) => (Calls ?? new()).Count(at => at >= sinceUtc);

    public void NoteCall(DateTime nowUtc)
    {
        Calls ??= new List<DateTime>();
        Calls.RemoveAll(at => at < nowUtc.AddDays(-7));
        Calls.Add(nowUtc);
    }

    public void NoteFetched(FocusTargetedReplay replay)
    {
        Fetched ??= new List<FocusTargetedReplay>();
        Fetched.RemoveAll(existing => existing.ReplayId == replay.ReplayId);
        Fetched.Add(replay);
        if (Fetched.Count > FetchedCap)
        {
            Fetched.RemoveRange(0, Fetched.Count - FetchedCap);
        }
    }
}

public sealed class FocusTargetedReplay
{
    public int ReplayId { get; set; }
    public string Hero { get; set; }
    public string AttributeId { get; set; }
    public DateTime FetchedAtUtc { get; set; }
}

public sealed class FocusTargetedStore
{
    public const string FileName = "targeted.json";

    internal static readonly HeroFocusJsonContext Json = new(
        new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        }
    );

    public FocusTargetedStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FilePath = Path.GetFullPath(path);
    }

    public string FilePath { get; }

    public static string PathFor(string dataDirectory) =>
        Path.Combine(HeroFocusPaths.FolderFor(dataDirectory), FileName);

    /// <summary>The state, or a fresh one when the file is missing or unreadable.</summary>
    public FocusTargetedState Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new FocusTargetedState();
            }

            FocusTargetedState state =
                JsonSerializer.Deserialize(File.ReadAllText(FilePath), Json.FocusTargetedState)
                ?? new FocusTargetedState();
            state.Seen ??= new();
            state.Tried ??= new();
            state.Fetched ??= new();
            state.Calls ??= new();
            return state;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new FocusTargetedState();
        }
    }

    public void Write(FocusTargetedState state) =>
        DurableFile.Replace(
            FilePath,
            JsonSerializer.Serialize(state ?? new(), Json.FocusTargetedState)
        );
}
