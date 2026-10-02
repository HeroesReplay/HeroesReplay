using System;
using HeroesReplay.Core.Obs;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsSelectionTests
{
    [Fact]
    public void Check_MatchingNames_IsOk()
    {
        ObsSelectionResult result = ObsSelection.Check(
            "HeroesReplay-live",
            "HeroesReplay",
            "HeroesReplay-live",
            "HeroesReplay"
        );

        Assert.True(result.Ok);
        Assert.Null(result.Reason);
        Assert.Contains("HeroesReplay-live", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_BlankExpectedNames_UseTheDefault()
    {
        Assert.True(ObsSelection.Check(null, " ", "HeroesReplay", "HeroesReplay").Ok);
        Assert.False(ObsSelection.Check(null, null, "Untitled", "HeroesReplay").Ok);
    }

    [Fact]
    public void Check_WrongProfile_NamesBothAndTheSetting()
    {
        ObsSelectionResult result = ObsSelection.Check(
            "HeroesReplay",
            "HeroesReplay",
            "Untitled",
            "HeroesReplay"
        );

        Assert.False(result.Ok);
        Assert.Equal("obs.profile_mismatch", result.Reason);
        Assert.Contains("'Untitled'", result.Detail, StringComparison.Ordinal);
        Assert.Contains("expected 'HeroesReplay'", result.Detail, StringComparison.Ordinal);
        Assert.Contains("OBS:ProfileName", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("OBS:SceneCollectionName", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_WrongCollection_IsACollectionMismatch()
    {
        ObsSelectionResult result = ObsSelection.Check(
            "HeroesReplay",
            "HeroesReplay",
            "HeroesReplay",
            "Scenes"
        );

        Assert.False(result.Ok);
        Assert.Equal("obs.collection_mismatch", result.Reason);
        Assert.Contains("'Scenes'", result.Detail, StringComparison.Ordinal);
        Assert.Contains("OBS:SceneCollectionName", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_BothWrong_ReportsTheProfileFirstAndDescribesBoth()
    {
        ObsSelectionResult result = ObsSelection.Check("A", "B", "C", "D");

        Assert.Equal(ObsSelection.ProfileMismatch, result.Reason);
        Assert.Contains("OBS:ProfileName", result.Detail, StringComparison.Ordinal);
        Assert.Contains("OBS:SceneCollectionName", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_IsCaseSensitive()
    {
        ObsSelectionResult result = ObsSelection.Check(
            "HeroesReplay",
            "HeroesReplay",
            "heroesreplay",
            "HeroesReplay"
        );

        Assert.Equal(ObsSelection.ProfileMismatch, result.Reason);
    }

    [Theory]
    [InlineData(null, "HeroesReplay")]
    [InlineData("HeroesReplay", "")]
    public void Check_MissingAnswer_FailsClosed(string profile, string collection)
    {
        ObsSelectionResult result = ObsSelection.Check(
            "HeroesReplay",
            "HeroesReplay",
            profile,
            collection
        );

        Assert.False(result.Ok);
        Assert.Equal(ObsSelection.Unreadable, result.Reason);
        Assert.Contains("were not started", result.Detail, StringComparison.Ordinal);
    }
}
