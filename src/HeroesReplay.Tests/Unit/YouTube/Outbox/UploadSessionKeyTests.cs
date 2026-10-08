using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using HeroesReplay.Core.YouTube.Outbox;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Outbox;

/// <summary>
/// #368: the resumable session URI YouTube returns repeats the API key (<c>key=</c>). The attempt
/// file never holds it, and a resumed send still carries it.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class UploadSessionKeyTests : IDisposable
{
    private const string FakeKey = "unit-test-yt-key-368";
    private const string UploadId = "upload_id=abc-368";
    private const string Bare =
        "https://www.googleapis.com/upload/youtube/v3/videos?part=snippet%2Cstatus&uploadType=resumable&"
        + UploadId;
    private const string Keyed =
        "https://www.googleapis.com/upload/youtube/v3/videos?part=snippet%2Cstatus&key="
        + FakeKey
        + "&uploadType=resumable&"
        + UploadId;

    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-session-key-" + Guid.NewGuid().ToString("N")
    );

    public UploadSessionKeyTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string Media => Path.Combine(root, "match.mp4");

    [Theory]
    [InlineData(Keyed, Bare)]
    [InlineData("https://h.test/u?key=" + FakeKey, "https://h.test/u")]
    [InlineData(
        "https://h.test/u?upload_id=a&KEY=" + FakeKey + "&x=1",
        "https://h.test/u?upload_id=a&x=1"
    )]
    [InlineData("https://h.test/u?upload_id=a&k%65y=" + FakeKey, "https://h.test/u?upload_id=a")]
    [InlineData(
        "https://h.test/u?upload_id=a&api_key=kept&monkey=kept",
        "https://h.test/u?upload_id=a&api_key=kept&monkey=kept"
    )]
    [InlineData(Bare, Bare)]
    [InlineData("https://h.test/u", "https://h.test/u")]
    public void WithoutKey_DropsOnlyTheKeyParameter(string uri, string expected)
    {
        Assert.Equal(expected, UploadSessionUri.WithoutKey(uri));
        Assert.False(UploadSessionUri.HasKey(UploadSessionUri.WithoutKey(uri)));
    }

    [Fact]
    public void WithKey_AddsTheConfiguredKeyBack_OnceAndOnlyWhenThereIsOne()
    {
        Assert.True(UploadSessionUri.HasKey(Keyed));
        Assert.False(UploadSessionUri.HasKey(Bare));
        Assert.False(UploadSessionUri.HasKey(null));
        Assert.Equal(Bare + "&key=" + FakeKey, UploadSessionUri.WithKey(Bare, FakeKey));
        Assert.Equal(Bare + "&key=" + FakeKey, UploadSessionUri.WithKey(Keyed, FakeKey));
        Assert.Equal(Bare, UploadSessionUri.WithKey(Keyed, null));
        Assert.Equal(Bare, UploadSessionUri.WithKey(Bare, " "));
        Assert.Equal(
            "https://h.test/u?key=a%2Bb",
            UploadSessionUri.WithKey("https://h.test/u", "a+b")
        );
    }

    [Fact]
    public async Task NoteSession_SavesTheSessionWithoutTheKey()
    {
        var outbox = new UploadOutbox(root);
        const string id = "replay-65745237-20261008090000";
        await StartSendAsync(outbox, id);

        UploadAttemptResult noted = await outbox.NoteSessionAsync(
            id,
            Keyed,
            Now,
            CancellationToken.None
        );

        Assert.True(noted.Succeeded, noted.Reason);
        Assert.Equal(Bare, noted.Manifest.SessionUri);
        // The same session as YouTube gave it is still the same session.
        Assert.True(
            (await outbox.NoteSessionAsync(id, Keyed, Now, CancellationToken.None)).Succeeded
        );
        AssertNoKeyOnDisk();
    }

    [Fact]
    public void Codec_NeverWritesTheKey()
    {
        var manifest = new UploadAttemptManifest
        {
            Schema = UploadAttemptManifest.SchemaVersion,
            AttemptId = "replay-65745237-20261008090000",
            ReplayId = 65745237,
            State = UploadAttemptState.AmbiguousUpload,
            MediaPath = Media,
            MediaSize = 1000,
            MediaHash = "len-1000",
            Revision = 4,
            UpdatedAtUtc = Now,
            SessionUri = Keyed,
        };

        string json = UploadAttemptManifestCodec.Write(manifest);

        Assert.DoesNotContain(FakeKey, json);
        Assert.Equal(Bare, UploadAttemptManifestCodec.Read(json, null).Manifest.SessionUri);
    }

    [Fact]
    public async Task AnAttemptSavedBeforeTheFix_LosesItsKeyTheFirstTimeItIsRead()
    {
        var outbox = new UploadOutbox(root);
        const string id = "replay-65773257-20261008090000";
        await StageAmbiguousAsync(outbox, id);
        long revision = (await outbox.LoadAsync(id, CancellationToken.None)).Manifest.Revision;
        WriteAsBeforeTheFix(outbox.ManifestPath(id));
        Assert.Contains(FakeKey, File.ReadAllText(outbox.ManifestPath(id)));

        UploadAttemptResult loaded = await new UploadOutbox(root).LoadAsync(
            id,
            CancellationToken.None
        );

        Assert.True(loaded.Succeeded, loaded.Reason);
        Assert.Equal(Bare, loaded.Manifest.SessionUri);
        Assert.Equal(UploadAttemptState.AmbiguousUpload, loaded.Manifest.State);
        Assert.Equal(revision, loaded.Manifest.Revision);
        AssertNoKeyOnDisk();
    }

    [Fact]
    public async Task ScrubSessionKeys_RewritesEverySavedAttemptThatHoldsTheKey()
    {
        var outbox = new UploadOutbox(root);
        await StageAmbiguousAsync(outbox, "replay-65745237-20261008090000");
        await StageAmbiguousAsync(outbox, "replay-65773257-20261008090000");
        await StageAmbiguousAsync(outbox, "replay-65700000-20261008090000");
        WriteAsBeforeTheFix(outbox.ManifestPath("replay-65745237-20261008090000"));
        WriteAsBeforeTheFix(outbox.ManifestPath("replay-65773257-20261008090000"));

        Assert.Equal(2, await outbox.ScrubSessionKeysAsync(CancellationToken.None));
        Assert.Equal(0, await outbox.ScrubSessionKeysAsync(CancellationToken.None));
        AssertNoKeyOnDisk();
        Assert.Equal(
            0,
            await new UploadOutbox(Path.Combine(root, "missing")).ScrubSessionKeysAsync(
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task Probe_AsksTheSavedSessionWithTheConfiguredKey()
    {
        var outbox = new UploadOutbox(root);
        const string id = "replay-65745237-20261008090000";
        await StageAmbiguousAsync(outbox, id);
        WriteAsBeforeTheFix(outbox.ManifestPath(id));
        UploadAttemptManifest saved = (await outbox.LoadAsync(id, CancellationToken.None)).Manifest;
        Uri asked = null;
        using var http = new HttpClient(
            new Answer(request =>
            {
                asked = request.RequestUri;
                return new HttpResponseMessage((HttpStatusCode)308);
            })
        );

        UploadSessionStatus status = await UploadSessionProbe.ProbeAsync(
            http,
            UploadSessionUri.WithKey(saved.SessionUri, FakeKey),
            1000,
            _ => null,
            CancellationToken.None
        );

        Assert.Equal(UploadSessionState.Incomplete, status.State);
        Assert.Contains(UploadId, asked.Query, StringComparison.Ordinal);
        Assert.Contains("key=" + FakeKey, asked.Query, StringComparison.Ordinal);
        AssertNoKeyOnDisk();
    }

    [Fact]
    public async Task Resume_AfterARestart_FinishesTheUploadThroughTheSavedSession()
    {
        byte[] media = new byte[1000];
        var youtube = new FakeYouTube(media.Length, held: 400);
        using YouTubeService service = new(
            new BaseClientService.Initializer
            {
                ApiKey = FakeKey,
                ApplicationName = "HeroesReplay.Tests",
                GZipEnabled = false,
                HttpClientFactory = new FakeFactory(youtube),
            }
        );
        const string id = "replay-65745237-20261008090000";
        var outbox = new UploadOutbox(root);
        await StartSendAsync(outbox, id);

        // The first process starts the session the way YouTubeUploader does, then stops.
        using (var first = new MemoryStream(media))
        {
            Uri started = await service
                .Videos.Insert(new Video(), "snippet,status", first, "video/*")
                .InitiateSessionAsync(CancellationToken.None);
            Assert.True(UploadSessionUri.HasKey(started.AbsoluteUri));
            Assert.True(
                (
                    await outbox.NoteSessionAsync(
                        id,
                        started.AbsoluteUri,
                        Now,
                        CancellationToken.None
                    )
                ).Succeeded
            );
        }

        AssertNoKeyOnDisk();

        // A new process reads the saved session and resumes it.
        UploadAttemptManifest saved = (
            await new UploadOutbox(root).LoadAsync(id, CancellationToken.None)
        ).Manifest;
        Assert.False(UploadSessionUri.HasKey(saved.SessionUri));
        using var second = new MemoryStream(media);
        VideosResource.InsertMediaUpload resume = service.Videos.Insert(
            new Video(),
            "snippet,status",
            second,
            "video/*"
        );
        Video uploaded = null;
        resume.ResponseReceived += video => uploaded = video;

        IUploadProgress progress = await resume.ResumeAsync(
            new Uri(UploadSessionUri.WithKey(saved.SessionUri, service.ApiKey)),
            CancellationToken.None
        );

        Assert.Equal(UploadStatus.Completed, progress.Status);
        Assert.Equal("vid-368", uploaded?.Id);
        Assert.Equal(new[] { "bytes */1000", "bytes 400-999/1000" }, youtube.Ranges);
        Assert.All(
            youtube.SessionQueries,
            query =>
            {
                Assert.Contains(UploadId, query, StringComparison.Ordinal);
                Assert.Contains("key=" + FakeKey, query, StringComparison.Ordinal);
            }
        );
        AssertNoKeyOnDisk();
    }

    private async Task StartSendAsync(UploadOutbox outbox, string id)
    {
        SavedDispatch sent = await outbox.SaveDispatchAsync(
            id,
            65745237,
            Media,
            1000,
            "len-1000",
            youtubeEnabled: true,
            dryRun: false,
            Now.AddMinutes(-3),
            CancellationToken.None
        );
        Assert.True(sent.MaySend, sent.Result.Reason);
    }

    private async Task StageAmbiguousAsync(UploadOutbox outbox, string id)
    {
        await StartSendAsync(outbox, id);
        Assert.True(
            (
                await outbox.NoteSessionAsync(id, Bare, Now.AddMinutes(-3), CancellationToken.None)
            ).Succeeded
        );
        Assert.True(
            (
                await outbox.MarkAmbiguousAsync(id, Now.AddMinutes(-1), CancellationToken.None)
            ).Succeeded
        );
    }

    /// <summary>What the uploader wrote before #368: the session URI exactly as YouTube gave it.</summary>
    private static void WriteAsBeforeTheFix(string manifestPath)
    {
        JsonNode json = JsonNode.Parse(File.ReadAllText(manifestPath));
        json[UploadAttemptManifest.SessionUriProperty] = Keyed;
        File.WriteAllText(
            manifestPath,
            json.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
        );
    }

    private void AssertNoKeyOnDisk()
    {
        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain(FakeKey, text);
            Assert.DoesNotContain("key=", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class Answer(Func<HttpRequestMessage, HttpResponseMessage> answer)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(answer(request));
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
    }

    /// <summary>
    /// The resumable upload protocol as YouTube answers it: the session start returns a session
    /// URI that repeats the request's query (the key too), a status query reports the bytes held,
    /// and the last chunk returns the video.
    /// </summary>
    private sealed class FakeYouTube(long length, long held) : HttpMessageHandler
    {
        public List<string> SessionQueries { get; } = new();

        public List<string> Ranges { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string query = request.RequestUri.Query;
            if (request.Method == HttpMethod.Post)
            {
                Assert.Contains("uploadType=resumable", query, StringComparison.Ordinal);
                var started = new HttpResponseMessage(HttpStatusCode.OK);
                started.Headers.Location = new Uri(request.RequestUri.AbsoluteUri + "&" + UploadId);
                return started;
            }

            Assert.Equal(HttpMethod.Put, request.Method);
            SessionQueries.Add(query);
            Ranges.Add(request.Content.Headers.GetValues("Content-Range").Single());
            byte[] body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            if (body.Length == 0)
            {
                var incomplete = new HttpResponseMessage((HttpStatusCode)308);
                incomplete.Headers.TryAddWithoutValidation("Range", "bytes=0-" + (held - 1));
                return incomplete;
            }

            Assert.Equal(length - held, body.Length);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"id\":\"vid-368\",\"status\":{\"privacyStatus\":\"private\"}}",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }
}
