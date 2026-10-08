using System;
using HeroesReplay.Core.Requests;
using Xunit;

namespace HeroesReplay.Tests.Unit.Requests;

/// <summary>#351: when a requested replay's download is given up, and how long a retry waits.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class RequestDownloadRetryTests
{
    [Theory]
    [InlineData(404, RequestDownloadVerdict.Fail)]
    [InlineData(410, RequestDownloadVerdict.Fail)]
    [InlineData(429, RequestDownloadVerdict.Retry)]
    [InlineData(500, RequestDownloadVerdict.Retry)]
    [InlineData(503, RequestDownloadVerdict.Retry)]
    [InlineData(400, RequestDownloadVerdict.Retry)]
    [InlineData(401, RequestDownloadVerdict.Retry)]
    [InlineData(403, RequestDownloadVerdict.Retry)]
    [InlineData(408, RequestDownloadVerdict.Retry)]
    public void Classify_OnlyAGoneFileFailsTheRequest(int status, RequestDownloadVerdict expected)
    {
        Assert.Equal(expected, RequestDownloadRetry.Classify(status));
    }

    [Fact]
    public void Classify_NoAnswerFromHeroesProfileIsRetried()
    {
        Assert.Equal(RequestDownloadVerdict.Retry, RequestDownloadRetry.Classify(null));
    }

    /// <summary>#361: only a 403 that says the replay was deleted is permanent.</summary>
    [Theory]
    [InlineData(403, "replay_deleted", RequestDownloadVerdict.Fail)]
    [InlineData(403, "REPLAY_DELETED", RequestDownloadVerdict.Fail)]
    [InlineData(403, "endpoint_not_in_plan", RequestDownloadVerdict.Retry)]
    [InlineData(403, null, RequestDownloadVerdict.Retry)]
    [InlineData(401, "replay_deleted", RequestDownloadVerdict.Retry)]
    [InlineData(429, "replay_deleted", RequestDownloadVerdict.Retry)]
    [InlineData(404, "replay_not_found", RequestDownloadVerdict.Fail)]
    public void Classify_A403IsPermanentOnlyWhenTheReplayWasDeleted(
        int status,
        string errorCode,
        RequestDownloadVerdict expected
    )
    {
        Assert.Equal(expected, RequestDownloadRetry.Classify(status, errorCode));
    }

    [Theory]
    [InlineData(403, "replay_deleted", "HTTP 403 replay_deleted")]
    [InlineData(404, null, "HTTP 404")]
    [InlineData(403, " ", "HTTP 403")]
    public void Describe_NamesTheStatusAndTheCode(int status, string errorCode, string expected)
    {
        Assert.Equal(expected, RequestDownloadRetry.Describe(status, errorCode));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(50, 30)]
    [InlineData(int.MaxValue, 30)]
    public void Delay_DoublesFromOneMinuteUpToHalfAnHour(int attempts, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), RequestDownloadRetry.Delay(attempts));
    }

    [Theory]
    [InlineData("2.57.0.98348", "2.57.0.98285", true)]
    [InlineData("2.57.0.98285", "2.57.0.98285", true)]
    [InlineData("2.56.1.97000", "2.57.0.98285", false)]
    [InlineData(null, "2.57.0.98285", true)]
    [InlineData("", "2.57.0.98285", true)]
    [InlineData("2.56.1.97000", null, true)]
    public void OnSupportedLine_FailsOnlyAKnownVersionBelowTheFloor(
        string version,
        string floor,
        bool expected
    )
    {
        Assert.Equal(expected, RequestDownloadRetry.OnSupportedLine(version, null, floor));
    }
}
