using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Metadata;

public static class YouTubeEntryBuilder
{
    public static YouTubeEntry Create(LoadedReplay loaded, YouTubeSettings youtube) =>
        Create(loaded, youtube, isCompleteRecording: false);

    public static YouTubeEntry Create(
        LoadedReplay loaded,
        YouTubeSettings youtube,
        bool isCompleteRecording,
        IReadOnlyList<Hero> heroCatalog = null,
        HeroStatsSnapshot heroStats = null,
        MatchInsights insights = null
    )
    {
        ReplayMediaPolicyInput facts = ReplayMediaFacts.From(
            loaded,
            false,
            false,
            false,
            heroCatalog
        );
        var input = new FullMatchMetadataInput
        {
            ReplayId = facts.ReplayId,
            GameDateUtc = facts.GameDateUtc,
            GameVersion = facts.GameVersion,
            Map = loaded?.Replay?.Map,
            MapAlternativeName = loaded?.Replay?.MapAlternativeName,
            HeroesProfileMap = loaded?.HeroesProfileReplay?.Map,
            GameMode = loaded?.HeroesProfileReplay?.GameType,
            Rank = facts.Rank,
            AverageMmr = facts.AverageMmr,
            FocusHero = facts.FocusHero,
            Roster = facts.Roster,
            HeroCatalog = heroCatalog,
            RecordAndUpload = facts.RecordAndUpload,
            RequestedBy = facts.RequestedBy,
            NamedPlayer =
                PlayerPriorityRequest.PlayerIndex(loaded?.Replay, loaded?.RewardQueueItem?.Request)
                != null,
            NotableEvents = facts.NotableEvents,
            HeroFocus = facts.HeroFocus,
            HeroFocusKda = facts.HeroFocus == null ? null : Kda(loaded),
            IsCompleteRecording = isCompleteRecording,
            HeroStats = heroStats,
            Insights = insights,
        };
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            input,
            new FullMatchMetadataOptions
            {
                IncludeSpoilers = false,
                IncludeRequestAttribution = true,
                CategoryId = string.IsNullOrWhiteSpace(youtube?.CategoryId)
                    ? "20"
                    : youtube.CategoryId,
                Titles = youtube?.Titles,
                TitleMarker = youtube?.TitlePrefix,
            }
        );

        var lines = new List<string> { "Twitch: https://twitch.tv/saltysadism" };
        if (metadata.DescriptionLines != null)
        {
            lines.AddRange(metadata.DescriptionLines);
        }

        return new YouTubeEntry
        {
            ReplayId = metadata.ReplayId ?? facts.ReplayId,
            TemplateVersion = metadata.TemplateVersion,
            Map = metadata.Map,
            GameType = metadata.GameMode,
            Rank = metadata.Rank,
            GameVersion = facts.GameVersion,
            Title = YouTubeListing.ApplyMarker(metadata.Title, youtube?.TitlePrefix),
            PrivacyStatus = youtube?.PrivacyStatus ?? "public",
            Requested = facts.ViewerRequested,
            // The featured hero spaces repeats: a request's hero, else the focus pick's (#433).
            Hero = facts.FocusHero ?? facts.HeroFocus?.Hero,
            HeroFocus =
                facts.HeroFocus == null
                    ? null
                    : new YouTubeHeroFocus
                    {
                        Hero = facts.HeroFocus.Hero,
                        Role = facts.HeroFocus.Role,
                        Build = facts.HeroFocus.Build,
                    },
            Heroes = HeroNames(facts.Roster),
            RecordedAtUtc = ToUtc(facts.GameDateUtc),
            CategoryId = metadata.CategoryId,
            DescriptionLines = lines.ToArray(),
            Tags = metadata.Tags == null ? [] : [.. metadata.Tags],
        };
    }

    /// <summary>The focus player's score screen kills, deaths and assists: <c>12/0/9</c>.</summary>
    private static string Kda(LoadedReplay loaded)
    {
        Player[] players = loaded?.Replay?.Players;
        int index = loaded?.FocusPick?.PlayerIndex ?? -1;
        ScoreResult score =
            players != null && index >= 0 && index < players.Length
                ? players[index]?.ScoreResult
                : null;
        return score == null ? null : $"{score.SoloKills}/{score.Deaths}/{score.Assists}";
    }

    private static string[] HeroNames(IReadOnlyList<ReplayMediaPlayer> roster)
    {
        if (roster == null || roster.Count == 0)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        foreach (ReplayMediaPlayer player in roster)
        {
            if (player != null && !string.IsNullOrWhiteSpace(player.Hero))
            {
                names.Add(player.Hero.Trim());
            }
        }

        return names.ToArray();
    }

    private static DateTimeOffset? ToUtc(DateTime? played)
    {
        if (played == null || played.Value == default)
        {
            return null;
        }

        DateTime value = played.Value;
        if (value.Kind == DateTimeKind.Local)
        {
            value = value.ToUniversalTime();
        }
        else if (value.Kind != DateTimeKind.Utc)
        {
            value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        return new DateTimeOffset(value);
    }
}
