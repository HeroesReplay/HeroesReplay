using System;
using Google.Apis.YouTube.v3.Data;

namespace HeroesReplay.Core.YouTube;

/// <summary>
/// The <c>videos.insert</c> body for a full match or a clip. Every video declares that it is
/// not made for kids. Nothing sets <c>contentDetails.contentRating</c>, so no video is
/// age-restricted. The insert is private and a public listing carries its publish time.
/// </summary>
public static class UploadBody
{
    public const string Part = "snippet,status";
    public const string DefaultCategoryId = "20";
    public const string Language = "en";
    public const string License = "youtube";

    public static Video Build(YouTubeEntry entry)
    {
        var status = new VideoStatus
        {
            PrivacyStatus = UploadVisibility.InsertStatus(entry?.PrivacyStatus),
            SelfDeclaredMadeForKids = false,
            ContainsSyntheticMedia = false,
            Embeddable = true,
            License = License,
        };
        DateTimeOffset? publishAt = UploadVisibility.PublishAt(
            entry?.DesiredPrivacyStatus,
            entry?.PublishAtUtc
        );
        if (publishAt != null)
        {
            status.PublishAtDateTimeOffset = publishAt.Value;
        }

        return new Video
        {
            Snippet = new VideoSnippet
            {
                Title = entry?.Title,
                Description = string.Join(
                    Environment.NewLine,
                    entry?.DescriptionLines ?? Array.Empty<string>()
                ),
                Tags = entry?.Tags,
                CategoryId = string.IsNullOrWhiteSpace(entry?.CategoryId)
                    ? DefaultCategoryId
                    : entry.CategoryId.Trim(),
                DefaultLanguage = Language,
                DefaultAudioLanguage = Language,
            },
            Status = status,
        };
    }
}
