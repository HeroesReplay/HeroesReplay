using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Services.Twitch;

public enum PredictionLedgerState
{
    Open,
    Pending,
    Settled,
    Terminal,
}

public enum PredictionIntent
{
    None,
    Resolve,
    Cancel,
}

public static class PredictionSessionKey
{
    public static string Format(int replayId, int attempt) => replayId + ":" + attempt;
}

public sealed class PredictionLedgerEntry
{
    public string SessionKey { get; set; }
    public int ReplayId { get; set; }
    public int Attempt { get; set; }
    public string PredictionId { get; set; }
    public string BlueOutcomeId { get; set; }
    public string RedOutcomeId { get; set; }
    public string BroadcasterId { get; set; }
    public string Title { get; set; }
    public string Map { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public PredictionLedgerState State { get; set; }
    public PredictionIntent Intent { get; set; }
    public int? WinningTeam { get; set; }
    public int FailureCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string TerminalReason { get; set; }
    public string SettledStatus { get; set; }
}

public sealed class PredictionLedgerDocument
{
    public List<PredictionLedgerEntry> Entries { get; set; } = new List<PredictionLedgerEntry>();
}

/// <summary>
/// Durable prediction ownership under the data directory. The key is the replay attempt, not the map title.
/// </summary>
public sealed class PredictionLedger
{
    public const string FileName = "prediction-ledger.json";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string path;
    private readonly List<PredictionLedgerEntry> entries = new List<PredictionLedgerEntry>();

    private PredictionLedger(string path)
    {
        this.path = path;
    }

    public IReadOnlyList<PredictionLedgerEntry> Entries => entries;

    public static string PathFor(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return null;
        }

        return Path.Combine(dataDirectory, FileName);
    }

    public static PredictionLedger Load(string path)
    {
        var ledger = new PredictionLedger(path);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return ledger;
        }

        try
        {
            PredictionLedgerDocument document =
                JsonSerializer.Deserialize<PredictionLedgerDocument>(
                    File.ReadAllText(path),
                    JsonOptions
                );
            if (document?.Entries == null)
            {
                return ledger;
            }

            foreach (PredictionLedgerEntry entry in document.Entries)
            {
                if (entry == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.SessionKey))
                {
                    entry.SessionKey = PredictionSessionKey.Format(entry.ReplayId, entry.Attempt);
                }

                ledger.entries.Add(entry);
            }
        }
        catch (JsonException)
        {
            return ledger;
        }
        catch (IOException)
        {
            return ledger;
        }

        return ledger;
    }

    public PredictionLedgerEntry[] Copy() => entries.ToArray();

    public PredictionLedgerEntry FindByPredictionId(string predictionId)
    {
        if (string.IsNullOrWhiteSpace(predictionId))
        {
            return null;
        }

        foreach (PredictionLedgerEntry entry in entries)
        {
            if (
                entry != null
                && string.Equals(entry.PredictionId, predictionId, StringComparison.Ordinal)
            )
            {
                return entry;
            }
        }

        return null;
    }

    public PredictionLedgerEntry FindUnsettled(int replayId)
    {
        PredictionLedgerEntry chosen = null;
        foreach (PredictionLedgerEntry entry in entries)
        {
            if (entry == null || entry.ReplayId != replayId || !IsUnsettled(entry))
            {
                continue;
            }

            if (chosen == null || entry.Attempt > chosen.Attempt)
            {
                chosen = entry;
            }
        }

        return chosen;
    }

    public PredictionLedgerEntry FindLatestUnsettled()
    {
        PredictionLedgerEntry chosen = null;
        foreach (PredictionLedgerEntry entry in entries)
        {
            if (entry == null || !IsUnsettled(entry))
            {
                continue;
            }

            if (chosen == null || entry.CreatedAt > chosen.CreatedAt)
            {
                chosen = entry;
            }
        }

        return chosen;
    }

    public int NextAttempt(int replayId)
    {
        int max = 0;
        foreach (PredictionLedgerEntry entry in entries)
        {
            if (entry != null && entry.ReplayId == replayId && entry.Attempt > max)
            {
                max = entry.Attempt;
            }
        }

        return max + 1;
    }

    public void Upsert(PredictionLedgerEntry entry)
    {
        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(entry.SessionKey))
        {
            entry.SessionKey = PredictionSessionKey.Format(entry.ReplayId, entry.Attempt);
        }

        int index = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i].SessionKey, entry.SessionKey, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
        {
            entries[index] = entry;
        }
        else
        {
            entries.Add(entry);
        }

        Save();
    }

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(
            new PredictionLedgerDocument { Entries = entries },
            JsonOptions
        );
        string temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    private static bool IsUnsettled(PredictionLedgerEntry entry) =>
        entry.State is PredictionLedgerState.Open or PredictionLedgerState.Pending;
}
