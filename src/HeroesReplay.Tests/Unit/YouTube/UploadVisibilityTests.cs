using System;
using HeroesReplay.Core.YouTube;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class UploadVisibilityTests
{
    [Fact]
    public void InsertStatus_IsPrivateWhateverTheListingWants()
    {
        Assert.Equal(UploadVisibility.Staged, UploadVisibility.InsertStatus("public"));
        Assert.Equal(UploadVisibility.Staged, UploadVisibility.InsertStatus("unlisted"));
        Assert.Equal("private", UploadVisibility.InsertStatus(null));
    }

    [Fact]
    public void CountsAsPublic_RequiresYouTubeAndTheListingToAgree()
    {
        Assert.True(UploadVisibility.CountsAsPublic("PUBLIC", "public"));
        Assert.False(UploadVisibility.CountsAsPublic("private", "public"));
        Assert.False(UploadVisibility.CountsAsPublic("public", "private"));
        Assert.False(UploadVisibility.CountsAsPublic(null, "public"));
        Assert.False(UploadVisibility.CountsAsPublic("public", null));
    }

    [Fact]
    public void PublishAt_StaysPrivateUntilYouTubeReportsPublic()
    {
        DateTimeOffset when = new(2026, 9, 30, 20, 0, 0, TimeSpan.Zero);

        Assert.Equal("private", UploadVisibility.InsertStatus("public"));
        Assert.Equal(when, UploadVisibility.PublishAt("public", when));
        Assert.Null(UploadVisibility.PublishAt("private", when));
        Assert.Null(UploadVisibility.PublishAt("public", null));
        Assert.True(UploadVisibility.ReconcileUntilPublic("private", "public"));
        Assert.False(UploadVisibility.ReconcileUntilPublic("public", "public"));
        Assert.False(UploadVisibility.ReconcileUntilPublic("private", "private"));
        Assert.False(UploadVisibility.CountsAsPublic("private", "public"));
    }
}
