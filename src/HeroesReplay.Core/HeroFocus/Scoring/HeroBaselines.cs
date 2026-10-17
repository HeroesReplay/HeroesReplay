using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Signatures;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.HeroFocus.Scoring;

/// <summary>
/// What each hero's signature stats usually are (#433): the newest values seen per hero and per
/// role, so 8 camps is judged against other Dehakas, not against a Li-Ming. A hero with too few
/// values is judged against its role.
/// </summary>
public sealed class HeroBaselines
{
    public const int CurrentVersion = 1;

    /// <summary>The card keys already counted, newest last, so a card is never counted twice.</summary>
    public const int FoldedCap = 2000;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Attribute id → stat → values, newest last.</summary>
    public Dictionary<string, Dictionary<string, List<double>>> Heroes { get; set; } = new();

    /// <summary>Role → stat → values, newest last.</summary>
    public Dictionary<string, Dictionary<string, List<double>>> Roles { get; set; } = new();

    public List<string> Folded { get; set; } = new();

    /// <summary>
    /// Counts every human player's signature stats. False when the card was counted already.
    /// </summary>
    public bool Add(FocusCard card, HeroSignatures signatures, int window)
    {
        if (string.IsNullOrWhiteSpace(card?.Key) || signatures == null)
        {
            return false;
        }

        Folded ??= new List<string>();
        if (Folded.Contains(card.Key, StringComparer.Ordinal))
        {
            return false;
        }

        window = Math.Max(1, window);
        foreach (FocusCardPlayer player in card.Players ?? new List<FocusCardPlayer>())
        {
            if (player.IsAi || string.IsNullOrWhiteSpace(player.AttributeId))
            {
                continue;
            }

            HeroSignature signature = signatures.For(
                player.UnitId,
                player.Hero,
                player.Role,
                player.Build?.Name
            );
            if (signature == null)
            {
                continue;
            }

            foreach (string stat in signature.Stats.Concat(signature.Low).Distinct())
            {
                if (player.Stats == null || !player.Stats.TryGetValue(stat, out double value))
                {
                    continue;
                }

                Push(Heroes, player.AttributeId, stat, value, window);
                if (!string.IsNullOrWhiteSpace(signature.Role))
                {
                    Push(Roles, signature.Role, stat, value, window);
                }
            }
        }

        Folded.Add(card.Key);
        if (Folded.Count > FoldedCap)
        {
            Folded.RemoveRange(0, Folded.Count - FoldedCap);
        }

        return true;
    }

    /// <summary>
    /// Where <paramref name="value"/> falls among the hero's values, 0 to 1 (ties count half):
    /// against the hero when it has <paramref name="minSamples"/> values, else its role. A stat
    /// where lower is better is turned around. Null when neither has enough values.
    /// </summary>
    public double? Percentile(
        string attributeId,
        string role,
        string stat,
        double value,
        bool lowerIsBetter,
        int minSamples
    )
    {
        List<double> values = Values(Heroes, attributeId, stat);
        if (values == null || values.Count < minSamples)
        {
            values = Values(Roles, role, stat);
        }

        if (values == null || values.Count < Math.Max(1, minSamples))
        {
            return null;
        }

        double below = 0;
        double same = 0;
        foreach (double seen in values)
        {
            if (seen < value)
            {
                below++;
            }
            else if (seen == value)
            {
                same++;
            }
        }

        double percentile = (below + same / 2) / values.Count;
        return lowerIsBetter ? 1 - percentile : percentile;
    }

    /// <summary>The values seen for one hero and stat; zero when none.</summary>
    public int Samples(string attributeId, string stat) =>
        Values(Heroes, attributeId, stat)?.Count ?? 0;

    private static void Push(
        Dictionary<string, Dictionary<string, List<double>>> table,
        string key,
        string stat,
        double value,
        int window
    )
    {
        if (!table.TryGetValue(key, out Dictionary<string, List<double>> stats))
        {
            stats = new Dictionary<string, List<double>>(StringComparer.Ordinal);
            table[key] = stats;
        }

        if (!stats.TryGetValue(stat, out List<double> values))
        {
            values = new List<double>();
            stats[stat] = values;
        }

        values.Add(Math.Round(value, 1));
        if (values.Count > window)
        {
            values.RemoveRange(0, values.Count - window);
        }
    }

    private static List<double> Values(
        Dictionary<string, Dictionary<string, List<double>>> table,
        string key,
        string stat
    ) =>
        !string.IsNullOrWhiteSpace(key)
        && table != null
        && table.TryGetValue(key, out Dictionary<string, List<double>> stats)
        && stats != null
        && stats.TryGetValue(stat, out List<double> values)
            ? values
            : null;
}

/// <summary>
/// <c>Data\Focus\baselines.json</c>. The download role is the only writer; the spectator reads
/// it with sharing on and starts from empty baselines when it cannot.
/// </summary>
public sealed class HeroBaselineStore
{
    public const string FileName = "baselines.json";

    internal static readonly HeroFocusJsonContext Json = new(
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        }
    );

    public HeroBaselineStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FilePath = path;
    }

    public string FilePath { get; }

    public static string PathFor(string dataDirectory) =>
        Path.Combine(HeroFocusPaths.FolderFor(dataDirectory), FileName);

    /// <summary>The baselines on disk, or empty ones when the file is missing or unreadable.</summary>
    public HeroBaselines Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new HeroBaselines();
            }

            using var stream = new FileStream(
                FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            HeroBaselines read = JsonSerializer.Deserialize(stream, Json.HeroBaselines);
            return read == null || read.Version != HeroBaselines.CurrentVersion
                ? new HeroBaselines()
                : read;
        }
        catch (JsonException)
        {
            return new HeroBaselines();
        }
        catch (IOException)
        {
            return new HeroBaselines();
        }
        catch (UnauthorizedAccessException)
        {
            return new HeroBaselines();
        }
    }

    public void Write(HeroBaselines baselines) =>
        DurableFile.Replace(
            FilePath,
            JsonSerializer.Serialize(baselines ?? new HeroBaselines(), Json.HeroBaselines)
        );
}
