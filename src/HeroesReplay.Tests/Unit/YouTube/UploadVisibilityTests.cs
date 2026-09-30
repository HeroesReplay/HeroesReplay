using HeroesReplay.Core.Services.YouTube;
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
}
