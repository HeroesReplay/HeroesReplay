using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using HeroesReplay.Core.HeroFocus.Cards;

namespace HeroesReplay.Core.HeroFocus.Ledger;

public enum FocusEntryState
{
    /// <summary>Held back from ordinary picks for the next focus slot.</summary>
    Reserved,

    /// <summary>Chosen as a focus replay and loaded.</summary>
    Picked,

    /// <summary>The focus replay played to the end.</summary>
    Played,

    /// <summary>Chosen, but deferred, failed, or expired before it played.</summary>
    Abandoned,
}

/// <summary>
/// Every focus pick (#433) and what it covered: hero, role, build, map, league, region and the
/// comps. The spectator is its only writer. Coverage counts Picked and Played entries; a
/// Reserved or Abandoned one covers nothing.
/// </summary>
public sealed class FocusLedger
{
    public const int CurrentVersion = 1;

    /// <summary>The newest entries kept.</summary>
    public const int EntryCap = 5000;

    public int Version { get; set; } = CurrentVersion;

    public List<FocusLedgerEntry> Entries { get; set; } = new();

    /// <summary>
    /// The Standard replays played since the last focus replay played, by id, so a replay marked
    /// spectated twice counts once. A focus is due once it holds <c>Focus:Every</c> − 1 of them.
    /// </summary>
    public List<int> SinceFocus { get; set; } = new();

    /// <summary>Picked and Played entries, oldest pick first.</summary>
    [JsonIgnore]
    public IReadOnlyList<FocusLedgerEntry> History =>
        (Entries ?? new List<FocusLedgerEntry>())
            .Where(entry => entry.Covers)
            .OrderBy(entry => entry.PickedAtUtc)
            .ToList();

    public FocusLedgerEntry Find(string key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : (Entries ?? new List<FocusLedgerEntry>()).LastOrDefault(entry =>
                string.Equals(entry.Key, key, StringComparison.Ordinal)
            );

    /// <summary>Adds or replaces the entry for its replay and keeps the newest <see cref="EntryCap"/>.</summary>
    public void Put(FocusLedgerEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry?.Key))
        {
            return;
        }

        Entries ??= new List<FocusLedgerEntry>();
        Entries.RemoveAll(existing =>
            string.Equals(existing.Key, entry.Key, StringComparison.Ordinal)
        );
        Entries.Add(entry);
        if (Entries.Count > EntryCap)
        {
            Entries.RemoveRange(0, Entries.Count - EntryCap);
        }
    }
}

public sealed class FocusLedgerEntry
{
    /// <summary>The replay's card key: its id as text, else its file name.</summary>
    public string Key { get; set; }

    public int? ReplayId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<FocusEntryState>))]
    public FocusEntryState State { get; set; }

    public DateTime PickedAtUtc { get; set; }

    public DateTime? PlayedAtUtc { get; set; }

    public DateTime? ReservedUntilUtc { get; set; }

    public int PlayerIndex { get; set; }

    /// <summary><c>Name#1234</c>. Local only: the stream and YouTube never show it.</summary>
    public string Player { get; set; }

    public int RegionId { get; set; }

    public string Hero { get; set; }

    public string AttributeId { get; set; }

    public string Role { get; set; }

    public string Build { get; set; }

    public double? BuildScore { get; set; }

    /// <summary>The build is a clear guide match, so the stream and YouTube name it.</summary>
    public bool BuildClear { get; set; }

    public string Map { get; set; }

    /// <summary>Bronze to Grandmaster, from the replay's rank.</summary>
    public string League { get; set; }

    public string Rank { get; set; }

    public string Region { get; set; }

    public string GameVersion { get; set; }

    /// <summary>The focus player's team's composition labels, joined (<c>Dive+Sustain</c>).</summary>
    public string AllyComp { get; set; }

    /// <summary>The other team's composition labels, joined.</summary>
    public string EnemyComp { get; set; }

    public FocusStrength Strength { get; set; }

    public double Quality { get; set; }

    public double Novelty { get; set; }

    public double Score { get; set; }

    /// <summary>Each part of the score, by name, for the logs and <c>focus coverage</c>.</summary>
    public Dictionary<string, double> Parts { get; set; } = new();

    /// <summary>A Picked or Played entry: it counts toward coverage and variety.</summary>
    [JsonIgnore]
    public bool Covers => State is FocusEntryState.Picked or FocusEntryState.Played;
}
