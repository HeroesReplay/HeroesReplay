using System;
using System.Collections.Generic;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Media;

namespace HeroesReplay.Core.Services.YouTube;

public static class YouTubeEntryBuilder
{
    public static YouTubeEntry Create(LoadedReplay loaded, YouTubeSettings youtube) =>
        Create(loaded, youtube, isCompleteRecording: false);

    public static YouTubeEntry Create(
        LoadedReplay loaded,
        YouTubeSettings youtube,
        bool isCompleteRecording,
        IReadOnlyList<Hero> heroCatalog = null
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
            NamedPlayer = loaded?.RewardQueueItem?.Request?.PlayerIndex != null,
            NotableEvents = facts.NotableEvents,
            IsCompleteRecording = isCompleteRecording,
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
            }
        );

        var lines = new List<string> { "Twitch: http://twitch.tv/saltysadism" };
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
            Hero = facts.FocusHero,
            Heroes = HeroNames(facts.Roster),
            RecordedAtUtc = ToUtc(facts.GameDateUtc),
            CategoryId = metadata.CategoryId,
            DescriptionLines = lines.ToArray(),
            Tags = metadata.Tags == null ? [] : [.. metadata.Tags],
        };
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
