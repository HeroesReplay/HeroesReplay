using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// Reads Heroes Profile's privacy feed (<c>players/privacy/changes</c>) and deletes what the
/// player lookups kept about each player who went private (#433). Heroes Profile's terms require
/// a cache of player data to poll it at least daily. The cursor (<c>since</c>, <c>after_id</c>)
/// is kept in <c>privacy.json</c>; the first read starts from now, because nothing was cached
/// before it.
/// </summary>
public sealed class PlayerPrivacySync
{
    public const string PrivacyPath = "players/privacy/changes";

    /// <summary>Rows per page; the endpoint allows up to 5,000.</summary>
    public const int PageSize = 1000;

    /// <summary>Pages read in one pass at most, so a backlog never runs unbounded.</summary>
    public const int MaxPages = 20;

    private readonly HeroesProfileCaller caller;
    private readonly PlayerHeroStatsStore store;
    private readonly PlayerStatsSettings settings;
    private readonly ILogger logger;
    private readonly Func<DateTimeOffset> clock;

    public PlayerPrivacySync(
        HeroesProfileCaller caller,
        PlayerHeroStatsStore store,
        PlayerStatsSettings settings,
        ILogger logger,
        Func<DateTimeOffset> clock = null
    )
    {
        this.caller = caller ?? throw new ArgumentNullException(nameof(caller));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.settings = settings ?? new PlayerStatsSettings();
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>True when the feed was last read longer than <c>PrivacyInterval</c> ago.</summary>
    public bool IsDue()
    {
        PlayerPrivacyCursor cursor = store.ReadPrivacy();
        return cursor?.LastReadUtc is not DateTimeOffset last
            || clock() - last >= settings.PrivacyInterval;
    }

    /// <summary>Reads the feed from the cursor and purges every player who went private. Returns how many were purged.</summary>
    public async Task<int> SyncAsync(CancellationToken cancellationToken)
    {
        PlayerPrivacyCursor cursor = store.ReadPrivacy();
        if (cursor?.Since == null)
        {
            // Nothing was cached before the first read, so nothing older can need purging.
            store.WritePrivacy(new PlayerPrivacyCursor { Since = clock(), LastReadUtc = clock() });
            return 0;
        }

        int purged = 0;
        for (int page = 0; page < MaxPages; page++)
        {
            HeroesProfileGlobalAnswer answer = await caller
                .GetAsync(PrivacyPath, Query(cursor), settings.JobTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (answer.StatusCode != 200)
            {
                throw HeroesProfileCaller.Failed(PrivacyPath, answer);
            }

            PrivacyPage read = Parse(answer.Body);
            foreach (PrivacyChange change in read.Changes)
            {
                if (
                    string.Equals(change.State, "private", StringComparison.OrdinalIgnoreCase)
                    && store.Purge(change.BattleTag, change.Region)
                )
                {
                    purged++;
                }
            }

            cursor = new PlayerPrivacyCursor
            {
                Since = read.NextSince ?? cursor.Since,
                AfterId = read.NextAfterId ?? cursor.AfterId,
                LastReadUtc = clock(),
            };
            store.WritePrivacy(cursor);
            if (!read.HasMore)
            {
                break;
            }
        }

        if (purged > 0)
        {
            logger.LogInformation(
                "Deleted the Heroes Profile records of {Count} players who made their profile private.",
                purged
            );
        }

        return purged;
    }

    private static IReadOnlyList<KeyValuePair<string, string>> Query(PlayerPrivacyCursor cursor) =>
        new[]
        {
            new KeyValuePair<string, string>(
                "since",
                cursor.Since?.ToString("o", CultureInfo.InvariantCulture)
            ),
            new KeyValuePair<string, string>(
                "after_id",
                cursor.AfterId?.ToString(CultureInfo.InvariantCulture)
            ),
            new KeyValuePair<string, string>(
                "limit",
                PageSize.ToString(CultureInfo.InvariantCulture)
            ),
        };

    /// <summary>
    /// <c>{"changes":[{"battletag","region","state","changed_at"}],"next_since","next_after_id","has_more"}</c>.
    /// </summary>
    internal static PrivacyPage Parse(string body)
    {
        var page = new PrivacyPage();
        if (string.IsNullOrWhiteSpace(body))
        {
            return page;
        }

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        if (
            root.TryGetProperty("changes", out JsonElement changes)
            && changes.ValueKind == JsonValueKind.Array
        )
        {
            foreach (JsonElement change in changes.EnumerateArray())
            {
                string battleTag =
                    change.TryGetProperty("battletag", out JsonElement tag)
                    && tag.ValueKind == JsonValueKind.String
                        ? tag.GetString()
                        : null;
                int region =
                    change.TryGetProperty("region", out JsonElement id)
                    && id.ValueKind == JsonValueKind.Number
                        ? id.GetInt32()
                        : 0;
                string state =
                    change.TryGetProperty("state", out JsonElement value)
                    && value.ValueKind == JsonValueKind.String
                        ? value.GetString()
                        : null;
                if (!string.IsNullOrWhiteSpace(battleTag))
                {
                    page.Changes.Add(new PrivacyChange(battleTag, region, state));
                }
            }
        }

        if (
            root.TryGetProperty("next_since", out JsonElement since)
            && since.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                since.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset parsed
            )
        )
        {
            page.NextSince = parsed;
        }

        if (
            root.TryGetProperty("next_after_id", out JsonElement after)
            && after.ValueKind == JsonValueKind.Number
        )
        {
            page.NextAfterId = after.GetInt64();
        }

        page.HasMore =
            root.TryGetProperty("has_more", out JsonElement more)
            && more.ValueKind == JsonValueKind.True;
        return page;
    }

    internal sealed record PrivacyChange(string BattleTag, int Region, string State);

    internal sealed class PrivacyPage
    {
        public List<PrivacyChange> Changes { get; } = new();
        public DateTimeOffset? NextSince { get; set; }
        public long? NextAfterId { get; set; }
        public bool HasMore { get; set; }
    }
}
