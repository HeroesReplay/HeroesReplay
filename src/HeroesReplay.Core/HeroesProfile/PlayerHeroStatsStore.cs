using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// The player lookups' cache (#433): one file per player under <c>Data\HeroesProfile\players</c>,
/// named from the region and a hash of the BattleTag, holding each hero asked about; plus the
/// calls made in the last 7 days (<c>calls.json</c>) for the weekly budget, and the privacy feed's
/// cursor (<c>privacy.json</c>). The download role writes it; a reader treats a file it cannot
/// read as missing.
/// </summary>
public sealed class PlayerHeroStatsStore
{
    public const string FolderName = "players";
    public const string CallsFileName = "calls.json";
    public const string PrivacyFileName = "privacy.json";

    internal static readonly HeroesProfileJsonContext Json = new(
        new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter<PlayerHeroLookupStatus>() },
        }
    );

    public PlayerHeroStatsStore(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = folder;
    }

    public string Folder { get; }

    /// <summary><c>Data\HeroesProfile\players</c>.</summary>
    public static string FolderFor(string dataDirectory) =>
        Path.Combine(dataDirectory, "HeroesProfile", FolderName);

    /// <summary>The file of one player: <c>&lt;region&gt;-&lt;hash&gt;.json</c>; the hash ignores case.</summary>
    public string PathFor(string battleTag, int region) =>
        Path.Combine(Folder, FileNameFor(battleTag, region));

    public static string FileNameFor(string battleTag, int region)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes((battleTag ?? string.Empty).Trim().ToUpperInvariant())
        );
        return region + "-" + Convert.ToHexString(hash, 0, 8).ToLowerInvariant() + ".json";
    }

    /// <summary>
    /// The cached answer for this player and hero while it is fresh: a known record younger than
    /// <paramref name="maxAge"/>, or a private, missing or unanswerable one younger than
    /// <paramref name="unknownRetryAfter"/>. Null when a lookup is due.
    /// </summary>
    public PlayerHeroLookup Fresh(
        string battleTag,
        int region,
        string attributeId,
        DateTimeOffset now,
        TimeSpan maxAge,
        TimeSpan unknownRetryAfter
    )
    {
        StoredPlayer player = Read(battleTag, region);
        if (player == null)
        {
            return null;
        }

        if (
            player.Status is PlayerHeroLookupStatus.Private or PlayerHeroLookupStatus.NotFound
            && now - player.StatusAtUtc < unknownRetryAfter
        )
        {
            return new PlayerHeroLookup(player.Status, Reason: "cached", Cached: true);
        }

        if (
            string.IsNullOrWhiteSpace(attributeId)
            || player.Heroes == null
            || !player.Heroes.TryGetValue(attributeId, out StoredHero hero)
        )
        {
            return null;
        }

        TimeSpan age = now - hero.FetchedAtUtc;
        if (hero.Status is PlayerHeroLookupStatus.Known or PlayerHeroLookupStatus.NoGames)
        {
            return age < maxAge
                ? new PlayerHeroLookup(hero.Status, hero.Stats, "cached", Cached: true)
                : null;
        }

        return age < unknownRetryAfter
            ? new PlayerHeroLookup(hero.Status, Reason: "cached", Cached: true)
            : null;
    }

    /// <summary>Keeps one hero's answer. A Private or NotFound answer is kept for the whole player.</summary>
    public void Write(
        string battleTag,
        int region,
        string attributeId,
        PlayerHeroLookup lookup,
        DateTimeOffset now
    )
    {
        if (string.IsNullOrWhiteSpace(battleTag) || lookup == null)
        {
            return;
        }

        StoredPlayer player =
            Read(battleTag, region)
            ?? new StoredPlayer { BattleTag = battleTag.Trim(), Region = region };
        player.Heroes ??= new Dictionary<string, StoredHero>(StringComparer.OrdinalIgnoreCase);
        if (lookup.Status is PlayerHeroLookupStatus.Private or PlayerHeroLookupStatus.NotFound)
        {
            player.Status = lookup.Status;
            player.StatusAtUtc = now;
            player.Heroes.Clear();
        }
        else if (!string.IsNullOrWhiteSpace(attributeId))
        {
            player.Status = PlayerHeroLookupStatus.Known;
            player.Heroes[attributeId] = new StoredHero
            {
                Status = lookup.Status,
                Stats = lookup.Stats,
                FetchedAtUtc = now,
            };
        }

        DurableFile.Replace(
            PathFor(battleTag, region),
            JsonSerializer.Serialize(player, Json.StoredPlayer)
        );
    }

    /// <summary>Deletes everything kept about a player. True when there was a file.</summary>
    public bool Purge(string battleTag, int region)
    {
        string path = PathFor(battleTag, region);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Replaces what is kept about a player who went private with a note that they are private,
    /// so their numbers are gone and every card learns it. False when nothing was kept.
    /// </summary>
    public bool MarkPrivate(string battleTag, int region, DateTimeOffset now)
    {
        StoredPlayer player = Read(battleTag, region);
        if (player == null)
        {
            return false;
        }

        player.Status = PlayerHeroLookupStatus.Private;
        player.StatusAtUtc = now;
        player.Heroes?.Clear();
        DurableFile.Replace(
            PathFor(battleTag, region),
            JsonSerializer.Serialize(player, Json.StoredPlayer)
        );
        return true;
    }

    /// <summary>True when the player's file says their profile is private.</summary>
    public bool IsPrivate(string battleTag, int region) =>
        Read(battleTag, region)?.Status == PlayerHeroLookupStatus.Private;

    /// <summary>The lookups made in the 7 days before <paramref name="now"/>.</summary>
    public int CallsThisWeek(DateTimeOffset now) =>
        ReadFile(Path.Combine(Folder, CallsFileName), Json.ListDateTimeOffset)
            ?.Count(at => now - at < TimeSpan.FromDays(7))
        ?? 0;

    /// <summary>Counts one lookup, dropping the ones older than 7 days.</summary>
    public void NoteCall(DateTimeOffset now)
    {
        string path = Path.Combine(Folder, CallsFileName);
        List<DateTimeOffset> calls = (ReadFile(path, Json.ListDateTimeOffset) ?? new())
            .Where(at => now - at < TimeSpan.FromDays(7))
            .ToList();
        calls.Add(now);
        DurableFile.Replace(path, JsonSerializer.Serialize(calls, Json.ListDateTimeOffset));
    }

    public PlayerPrivacyCursor ReadPrivacy() =>
        ReadFile(Path.Combine(Folder, PrivacyFileName), Json.PlayerPrivacyCursor);

    public void WritePrivacy(PlayerPrivacyCursor cursor) =>
        DurableFile.Replace(
            Path.Combine(Folder, PrivacyFileName),
            JsonSerializer.Serialize(cursor ?? new PlayerPrivacyCursor(), Json.PlayerPrivacyCursor)
        );

    public const string PrunerName = "player-stats";

    /// <summary>The player cache pruner for the housekeeping pass (#492).</summary>
    public static IPruner Pruner(AppSettings settings) =>
        new Pruner(
            PrunerName,
            "Player lookups (Data\\HeroesProfile\\players) a lookup would no longer reuse: older than PlayerStats:MaxAge, or a private mark older than PlayerStats:PrivateKeep. calls.json and privacy.json stay.",
            now =>
                string.IsNullOrWhiteSpace(settings?.Location?.DataDirectory)
                    ? new PrunePlan(PrunerName)
                    : new PlayerHeroStatsStore(
                        FolderFor(settings.Location.DataDirectory)
                    ).PlanPrune(
                        now,
                        settings.HeroesProfileApi?.PlayerStats ?? new PlayerStatsSettings()
                    )
        );

    /// <summary>
    /// The player files nothing reuses any more (#492): about 600 new a day on the stream PC, and
    /// nothing removed one. A record older than <see cref="PlayerStatsSettings.MaxAge"/> (and
    /// <see cref="PlayerStatsSettings.UnknownRetryAfter"/>) is looked up again anyway; a private
    /// mark, which tells focus cards to drop the player's numbers, stays for
    /// <see cref="PlayerStatsSettings.PrivateKeep"/>. Writes nothing.
    /// </summary>
    public PrunePlan PlanPrune(DateTimeOffset utcNow, PlayerStatsSettings settings)
    {
        var plan = new PrunePlan(PrunerName);
        if (!Directory.Exists(Folder))
        {
            return plan;
        }

        settings ??= new PlayerStatsSettings();
        TimeSpan reuse =
            settings.MaxAge > settings.UnknownRetryAfter
                ? settings.MaxAge
                : settings.UnknownRetryAfter;
        foreach (FileInfo file in new DirectoryInfo(Folder).GetFiles("*.json"))
        {
            if (
                string.Equals(file.Name, CallsFileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.Name, PrivacyFileName, StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            StoredPlayer player = ReadFile(file.FullName, Json.StoredPlayer);
            DateTimeOffset newest = Newest(player) ?? file.LastWriteTimeUtc;
            bool marked = player?.Status == PlayerHeroLookupStatus.Private;
            TimeSpan limit = marked ? settings.PrivateKeep : reuse;
            if (limit > TimeSpan.Zero && utcNow - newest >= limit)
            {
                plan.Add(
                    PruneItem.ForFile(
                        file,
                        marked
                            ? "private mark older than HeroesProfileApi:PlayerStats:PrivateKeep"
                            : "player lookup older than HeroesProfileApi:PlayerStats:MaxAge: it would be looked up again"
                    )
                );
            }
        }

        return plan;
    }

    private static DateTimeOffset? Newest(StoredPlayer player)
    {
        if (player == null)
        {
            return null;
        }

        DateTimeOffset newest = player.StatusAtUtc;
        foreach (StoredHero hero in player.Heroes?.Values ?? Enumerable.Empty<StoredHero>())
        {
            if (hero != null && hero.FetchedAtUtc > newest)
            {
                newest = hero.FetchedAtUtc;
            }
        }

        return newest == default ? null : newest;
    }

    private StoredPlayer Read(string battleTag, int region) =>
        string.IsNullOrWhiteSpace(battleTag)
            ? null
            : ReadFile(PathFor(battleTag, region), Json.StoredPlayer);

    private static T ReadFile<T>(string path, JsonTypeInfo<T> type)
        where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            return JsonSerializer.Deserialize(stream, type);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal sealed class StoredPlayer
    {
        public string BattleTag { get; set; }
        public int Region { get; set; }
        public PlayerHeroLookupStatus Status { get; set; } = PlayerHeroLookupStatus.Known;
        public DateTimeOffset StatusAtUtc { get; set; }
        public Dictionary<string, StoredHero> Heroes { get; set; } = new();
    }

    internal sealed class StoredHero
    {
        public PlayerHeroLookupStatus Status { get; set; }
        public PlayerHeroStats Stats { get; set; }
        public DateTimeOffset FetchedAtUtc { get; set; }
    }
}

/// <summary>Where the privacy feed was read up to (<c>since</c> and <c>after_id</c>).</summary>
public sealed class PlayerPrivacyCursor
{
    public DateTimeOffset? Since { get; set; }
    public long? AfterId { get; set; }
    public DateTimeOffset? LastReadUtc { get; set; }
}
