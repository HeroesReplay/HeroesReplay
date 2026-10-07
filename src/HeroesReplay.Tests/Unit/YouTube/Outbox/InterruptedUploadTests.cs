using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;
using HeroesReplay.Core.YouTube.Outbox;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Outbox;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class InterruptedUploadTests : IDisposable
{
    private const string Session =
        "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&upload_id=abc";

    private static readonly DateTimeOffset Now = new(2026, 10, 7, 17, 20, 0, TimeSpan.Zero);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-interrupted-" + Guid.NewGuid().ToString("N")
    );

    public InterruptedUploadTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Plan_ASavedSessionIsCheckedBeforeAnythingIsSent()
    {
        Assert.Equal(
            InterruptedUploadAction.Probe,
            InterruptedUpload.Plan(0, 5, hasSession: true, replayOnChannel: true)
        );
    }

    [Fact]
    public void Plan_NoSessionMeansYouTubeNeverGotTheFile_UnlessTheReplayIsOnTheChannel()
    {
        Assert.Equal(
            InterruptedUploadAction.Restart,
            InterruptedUpload.Plan(0, 5, hasSession: false, replayOnChannel: false)
        );
        Assert.Equal(
            InterruptedUploadAction.AlreadyOnYouTube,
            InterruptedUpload.Plan(0, 5, hasSession: false, replayOnChannel: true)
        );
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(0, 0)]
    public void Plan_UsedRetriesLeaveItForAnOperator(int used, int max)
    {
        Assert.Equal(
            InterruptedUploadAction.Exhausted,
            InterruptedUpload.Plan(used, max, hasSession: true, replayOnChannel: false)
        );
    }

    [Fact]
    public void AfterProbe_ACompleteSessionIsConfirmedNotSentAgain()
    {
        Assert.Equal(
            InterruptedUploadAction.Confirm,
            InterruptedUpload.AfterProbe(UploadSessionState.Complete, Now.AddDays(1), Now)
        );
    }

    [Fact]
    public void AfterProbe_AnIncompleteSessionResumesWhileItsTimeIsAhead()
    {
        Assert.Equal(
            InterruptedUploadAction.Resume,
            InterruptedUpload.AfterProbe(UploadSessionState.Incomplete, Now.AddHours(3), Now)
        );
        Assert.Equal(
            InterruptedUploadAction.Resume,
            InterruptedUpload.AfterProbe(UploadSessionState.Incomplete, null, Now)
        );
    }

    [Fact]
    public void AfterProbe_AnIncompleteSessionPastItsTimeIsRescheduled()
    {
        Assert.Equal(
            InterruptedUploadAction.Reschedule,
            InterruptedUpload.AfterProbe(UploadSessionState.Incomplete, Now.AddHours(-1), Now)
        );
    }

    [Fact]
    public void AfterProbe_AGoneSessionMayHaveFinished_AndAFailedCheckWaits()
    {
        Assert.Equal(
            InterruptedUploadAction.SessionGone,
            InterruptedUpload.AfterProbe(UploadSessionState.Gone, Now.AddHours(1), Now)
        );
        Assert.Equal(
            InterruptedUploadAction.CheckFailed,
            InterruptedUpload.AfterProbe(UploadSessionState.Unknown, Now.AddHours(1), Now)
        );
    }

    [Fact]
    public void ResumeAtNewTime_OnlyWhenTheNewTimeIsNow()
    {
        Assert.True(InterruptedUpload.ResumeAtNewTime(Now, Now));
        Assert.True(InterruptedUpload.ResumeAtNewTime(Now.AddMinutes(1), Now));
        Assert.True(InterruptedUpload.ResumeAtNewTime(null, Now));
        Assert.False(InterruptedUpload.ResumeAtNewTime(Now.AddHours(2), Now));
    }

    [Fact]
    public void Retries_CountPerAttemptAndSurviveANewProcess()
    {
        var first = new InterruptedUploadRetries(root);
        Assert.Equal(1, first.Add("replay-1-20261007161717", Now));
        Assert.Equal(2, first.Add("replay-1-20261007161717", Now));
        Assert.Equal(1, first.Add("replay-2-20261007161717", Now));

        var restarted = new InterruptedUploadRetries(root);
        Assert.Equal(2, restarted.Count("replay-1-20261007161717"));
        restarted.Clear("replay-1-20261007161717");
        Assert.Equal(0, new InterruptedUploadRetries(root).Count("replay-1-20261007161717"));
        Assert.Equal(1, new InterruptedUploadRetries(root).Count("replay-2-20261007161717"));
    }

    [Fact]
    public void Retries_AnUnreadableFileStartsOver()
    {
        File.WriteAllText(Path.Combine(root, InterruptedUploadRetries.FileName), "{not json");

        Assert.Equal(0, new InterruptedUploadRetries(root).Count("replay-1"));
        Assert.Equal(1, new InterruptedUploadRetries(root).Add("replay-1", Now));
    }

    [Fact]
    public void AbandonSession_DropsTheSessionAndGoesBackToPending()
    {
        UploadAttemptResult abandoned = UploadAttemptMachine.AbandonSession(Ambiguous(), Now);

        Assert.True(abandoned.Succeeded, abandoned.Reason);
        Assert.Equal(UploadAttemptState.UploadPending, abandoned.Manifest.State);
        Assert.Null(abandoned.Manifest.SessionUri);
        Assert.Null(
            UploadAttemptMachine.SuccessorRejection(Ambiguous(), abandoned.Manifest.WithRevision(8))
        );
    }

    [Fact]
    public void AbandonSession_OnlyFromAnInterruptedSendWithoutAVideo()
    {
        UploadAttemptManifest uploading = Ambiguous(UploadAttemptState.Uploading);
        Assert.False(UploadAttemptMachine.AbandonSession(uploading, Now).Succeeded);
        Assert.False(
            UploadAttemptMachine
                .AbandonSession(
                    Ambiguous(),
                    new DateTimeOffset(Now.DateTime, TimeSpan.FromHours(1))
                )
                .Succeeded
        );
    }

    [Fact]
    public async Task Outbox_AbandonedAttemptDispatchesAFreshInsert()
    {
        var outbox = new UploadOutbox(root);
        const string id = "replay-65719257-20261007161717";
        await StageAmbiguousAsync(outbox, id);

        UploadAttemptResult abandoned = await outbox.AbandonSessionAsync(
            id,
            Now,
            CancellationToken.None
        );
        Assert.True(abandoned.Succeeded, abandoned.Reason);

        SavedDispatch dispatched = await outbox.SaveDispatchAsync(
            id,
            65719257,
            Media,
            2535447752,
            "len-2535447752",
            youtubeEnabled: true,
            dryRun: false,
            Now.AddSeconds(1),
            CancellationToken.None
        );
        Assert.True(dispatched.MaySend);
        Assert.Equal(UploadAttemptState.Uploading, dispatched.Result.Manifest.State);
        Assert.Null(dispatched.Result.Manifest.SessionUri);
    }

    [Fact]
    public async Task Outbox_AnAutomaticRetryResumesTheSameSession()
    {
        var outbox = new UploadOutbox(root);
        const string id = "replay-65719257-20261007161717";
        await StageAmbiguousAsync(outbox, id);

        SavedDispatch held = await outbox.SaveDispatchAsync(
            id,
            65719257,
            Media,
            2535447752,
            "len-2535447752",
            youtubeEnabled: true,
            dryRun: false,
            Now,
            CancellationToken.None
        );
        Assert.False(held.MaySend);

        SavedDispatch retried = await outbox.SaveDispatchAsync(
            id,
            65719257,
            Media,
            2535447752,
            "len-2535447752",
            youtubeEnabled: true,
            dryRun: false,
            Now,
            CancellationToken.None,
            operatorRetry: true
        );
        Assert.True(retried.MaySend);
        Assert.Equal(Session, retried.Result.Manifest.SessionUri);
    }

    [Fact]
    public async Task Probe_IncompleteSessionReportsTheBytesYouTubeHolds()
    {
        UploadSessionStatus status = await Probe(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)308);
            response.Headers.TryAddWithoutValidation("Range", "bytes=0-1090519039");
            return response;
        });

        Assert.Equal(UploadSessionState.Incomplete, status.State);
        Assert.Equal(1090519040, status.BytesReceived);
    }

    [Fact]
    public async Task Probe_AsksWithAnEmptyBodyAndTheTotalSize()
    {
        HttpMethod method = null;
        string range = null;
        long? length = null;
        await Probe(request =>
        {
            method = request.Method;
            range = request.Content.Headers.ContentRange.ToString();
            length = request.Content.Headers.ContentLength;
            return new HttpResponseMessage((HttpStatusCode)308);
        });

        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("bytes */2535447752", range);
        Assert.Equal(0, length);
    }

    [Fact]
    public async Task Probe_CompleteSessionReturnsTheVideo()
    {
        UploadSessionStatus status = await Probe(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") },
            _ => new Video
            {
                Id = "vid-1",
                Status = new VideoStatus { PrivacyStatus = "private" },
            }
        );

        Assert.Equal(UploadSessionState.Complete, status.State);
        Assert.Equal("vid-1", status.Video.Id);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, UploadSessionState.Gone)]
    [InlineData(HttpStatusCode.Gone, UploadSessionState.Gone)]
    [InlineData(HttpStatusCode.InternalServerError, UploadSessionState.Unknown)]
    public async Task Probe_OtherAnswers(HttpStatusCode code, UploadSessionState expected)
    {
        UploadSessionStatus status = await Probe(_ => new HttpResponseMessage(code));

        Assert.Equal(expected, status.State);
        Assert.Equal((int)code, status.HttpStatus);
    }

    [Fact]
    public async Task Probe_ANetworkFailureIsUnknownNotAnError()
    {
        UploadSessionStatus status = await Probe(_ => throw new HttpRequestException("reset"));

        Assert.Equal(UploadSessionState.Unknown, status.State);
        Assert.Contains("reset", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_AFinishedSessionWithoutAVideoIdIsUnknown()
    {
        UploadSessionStatus status = await Probe(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") },
            _ => new Video()
        );

        Assert.Equal(UploadSessionState.Unknown, status.State);
    }

    private const string Media = @"C:\heroesreplay\Data\Contexts\65719257\2026-10-05 01-35-32.mp4";

    private static async Task<UploadSessionStatus> Probe(
        Func<HttpRequestMessage, HttpResponseMessage> answer,
        Func<string, Video> readVideo = null
    )
    {
        using var http = new HttpClient(new Handler(answer));
        return await UploadSessionProbe.ProbeAsync(
            http,
            Session,
            2535447752,
            readVideo ?? (_ => null),
            CancellationToken.None
        );
    }

    private static async Task StageAmbiguousAsync(UploadOutbox outbox, string id)
    {
        SavedDispatch sent = await outbox.SaveDispatchAsync(
            id,
            65719257,
            Media,
            2535447752,
            "len-2535447752",
            youtubeEnabled: true,
            dryRun: false,
            Now.AddMinutes(-3),
            CancellationToken.None
        );
        Assert.True(sent.MaySend, sent.Result.Reason);
        Assert.True(
            (
                await outbox.NoteSessionAsync(
                    id,
                    Session,
                    Now.AddMinutes(-3),
                    CancellationToken.None
                )
            ).Succeeded
        );
        Assert.True(
            (
                await outbox.MarkAmbiguousAsync(id, Now.AddMinutes(-1), CancellationToken.None)
            ).Succeeded
        );
    }

    private static UploadAttemptManifest Ambiguous(
        UploadAttemptState state = UploadAttemptState.AmbiguousUpload
    ) =>
        new()
        {
            Schema = UploadAttemptManifest.SchemaVersion,
            AttemptId = "replay-65719257-20261007161717",
            ReplayId = 65719257,
            State = state,
            MediaPath = Media,
            MediaSize = 2535447752,
            MediaHash = "len-2535447752",
            Revision = 7,
            UpdatedAtUtc = Now.AddMinutes(-1),
            SessionUri = Session,
        };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(answer(request));
    }
}
