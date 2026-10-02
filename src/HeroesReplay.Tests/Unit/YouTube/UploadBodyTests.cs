using System;
using Google.Apis.YouTube.v3.Data;
using HeroesReplay.Core.YouTube;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class UploadBodyTests
{
    private static readonly DateTimeOffset Slot = new(2026, 10, 2, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_DeclaresAFullMatchNotMadeForKidsAndNotAgeRestricted()
    {
        Video video = UploadBody.Build(
            new YouTubeEntry
            {
                Title = "Volskaya Foundry - Storm League - Diamond - 65389750",
                DescriptionLines = new[] { "Twitch: https://twitch.tv/saltysadism", "Full match." },
                Tags = new[] { "Volskaya Foundry", "Storm League" },
                CategoryId = "20",
                PrivacyStatus = "private",
                DesiredPrivacyStatus = "public",
                PublishAtUtc = Slot,
                ReplayId = 65389750,
            }
        );

        Assert.False(video.Status.SelfDeclaredMadeForKids);
        Assert.Null(video.ContentDetails);
        Assert.False(video.Status.ContainsSyntheticMedia);
        Assert.True(video.Status.Embeddable);
        Assert.Equal("youtube", video.Status.License);
        Assert.Equal("private", video.Status.PrivacyStatus);
        Assert.Equal(Slot, video.Status.PublishAtDateTimeOffset);
        Assert.Equal("Volskaya Foundry - Storm League - Diamond - 65389750", video.Snippet.Title);
        Assert.Equal(
            "Twitch: https://twitch.tv/saltysadism" + Environment.NewLine + "Full match.",
            video.Snippet.Description
        );
        Assert.Equal(new[] { "Volskaya Foundry", "Storm League" }, video.Snippet.Tags);
        Assert.Equal("20", video.Snippet.CategoryId);
        Assert.Equal("en", video.Snippet.DefaultLanguage);
        Assert.Equal("en", video.Snippet.DefaultAudioLanguage);
        Assert.Equal("snippet,status", UploadBody.Part);
    }

    [Fact]
    public void Build_DeclaresAClipNotMadeForKidsAndFillsAMissingCategory()
    {
        Video video = UploadBody.Build(
            new YouTubeEntry
            {
                Title = "Li-Ming - pentakill - Alterac Pass - 65550001",
                DescriptionLines = new[] { "clip:65550001:pentakill:Li-Ming" },
                Tags = new[] { "pentakill", "Li-Ming", "Alterac Pass" },
                CategoryId = null,
                PrivacyStatus = "private",
                DesiredPrivacyStatus = "public",
                PublishAtUtc = Slot,
                ReplayId = 65550001,
            }
        );

        Assert.False(video.Status.SelfDeclaredMadeForKids);
        Assert.Null(video.ContentDetails);
        Assert.Equal("20", video.Snippet.CategoryId);
        Assert.Equal(Slot, video.Status.PublishAtDateTimeOffset);
    }

    [Fact]
    public void Build_InsertsPrivateWithNoPublishTimeWhenTheListingStaysPrivate()
    {
        Video video = UploadBody.Build(
            new YouTubeEntry
            {
                Title = "[TEST] Sky Temple - 1",
                PrivacyStatus = "private",
                DesiredPrivacyStatus = "private",
                PublishAtUtc = Slot,
            }
        );
        Video empty = UploadBody.Build(null);

        Assert.Equal("private", video.Status.PrivacyStatus);
        Assert.Null(video.Status.PublishAtDateTimeOffset);
        Assert.False(video.Status.SelfDeclaredMadeForKids);
        Assert.Equal(string.Empty, video.Snippet.Description);
        Assert.Equal("private", empty.Status.PrivacyStatus);
        Assert.False(empty.Status.SelfDeclaredMadeForKids);
    }
}
