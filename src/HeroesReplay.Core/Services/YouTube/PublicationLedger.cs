using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HeroesReplay.Core.Services.YouTube;

public sealed class PublicationLedger
{
    public List<DateTimeOffset> PublicAtUtc { get; set; } = new();
    public List<bool> Requested { get; set; } = new();
    public string LastMap { get; set; }
    public DateTimeOffset? LastMapUtc { get; set; }
    public string LastHero { get; set; }
    public DateTimeOffset? LastHeroUtc { get; set; }
    public DateTimeOffset? LastPublicUtc { get; set; }
    public int InsertsThisQuotaDay { get; set; }
    public DateTimeOffset QuotaDay { get; set; }
    public DateTimeOffset? LastInsertUtc { get; set; }
    public int StuckPrivate { get; set; }
}

/// <summary>
/// Insert times survive a restart so the daily and weekly caps still apply.
/// </summary>
public static class PublicationLedgerStore
{
    public const string FileName = "youtube-publication.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string PathFor(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return null;
        }

        return Path.Combine(dataDirectory, FileName);
    }

    public static PublicationLedger Load(string dataDirectory)
    {
        string path = PathFor(dataDirectory);
        if (path == null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PublicationLedger>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    public static void Save(string dataDirectory, PublicationLedger ledger)
    {
        string path = PathFor(dataDirectory);
        if (path == null || ledger == null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(ledger, Json));
        File.Move(temp, path, overwrite: true);
    }
}
