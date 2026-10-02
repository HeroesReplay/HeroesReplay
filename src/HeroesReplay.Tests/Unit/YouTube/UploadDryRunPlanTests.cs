using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Outbox;
using HeroesReplay.Core.YouTube.Publication;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class UploadDryRunPlanTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-dry-plan-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessRecording_DryRunWritesThePublishTimeALiveSendWouldUse()
    {
        AppSettings settings = Settings();
        string first = await StageAsync(settings, 65550001, "Alterac Pass", "Diamond");
        string second = await StageAsync(settings, 65550002, "Braxis Holdout", "Gold");
        var uploader = new YouTubeUploader(
            NullLogger<YouTubeUploader>.Instance,
            settings,
            new CancellationTokenSource()
        )
        {
            ReplaySessionFilePath = Path.Combine(root, "replay-sessions.txt"),
        };

        await uploader.ProcessRecording(first);
        await uploader.ProcessRecording(second);

        using JsonDocument firstPlan = Receipt(first);
        using JsonDocument secondPlan = Receipt(second);
        DateTimeOffset firstAt = firstPlan
            .RootElement.GetProperty("PublishAtUtc")
            .GetDateTimeOffset();
        DateTimeOffset secondAt = secondPlan
            .RootElement.GetProperty("PublishAtUtc")
            .GetDateTimeOffset();
        Assert.Equal(TimeSpan.FromHours(2), secondAt - firstAt);
        Assert.Equal("granted", firstPlan.RootElement.GetProperty("Schedule").GetString());
        Assert.Equal("ready", firstPlan.RootElement.GetProperty("ScheduleReason").GetString());
        Assert.Equal("interval", secondPlan.RootElement.GetProperty("ScheduleReason").GetString());
        Assert.Equal("private", secondPlan.RootElement.GetProperty("PrivacyStatus").GetString());
        Assert.Equal(
            "public",
            secondPlan.RootElement.GetProperty("DesiredPrivacyStatus").GetString()
        );
        Assert.False(secondPlan.RootElement.GetProperty("SelfDeclaredMadeForKids").GetBoolean());
        Assert.Equal("20", secondPlan.RootElement.GetProperty("CategoryId").GetString());
        Assert.True(File.Exists(Path.Combine(root, "publication-reservations-dry-run.txt")));
        Assert.False(File.Exists(Path.Combine(root, "publication-reservations.txt")));
        Assert.False(File.Exists(Path.Combine(root, YouTubeQuotaUnits.FileName)));
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    private AppSettings Settings()
    {
        return new AppSettings
        {
            YouTube = new YouTubeSettings
            {
                DryRun = true,
                Enabled = true,
                PrivacyStatus = "public",
                EntryFileName = "youtube-entry.json",
                EntryFileNameUploaded = "youtube-entry-uploaded.json",
                ReadyStableReads = 1,
                ReadyPollMilliseconds = 20,
            },
            Location = new LocationSettings { DataDirectory = root },
            ReplayMedia = PublicationSchedule.CanarySettings(),
        };
    }

    /// <summary>
    /// A finished recording with its entry, and the media decision the spectator saved for it.
    /// </summary>
    private async Task<string> StageAsync(
        AppSettings settings,
        int replayId,
        string map,
        string rank
    )
    {
        string context = Path.Combine(settings.ContextsDirectory, replayId.ToString());
        Directory.CreateDirectory(context);
        string recording = Path.Combine(context, "match.mp4");
        await File.WriteAllBytesAsync(recording, new byte[] { 0, 0, 0, 24, 102, 116, 121, 112 });
        await File.WriteAllTextAsync(
            Path.Combine(context, "youtube-entry.json"),
            JsonSerializer.Serialize(
                new YouTubeEntry
                {
                    Title = map + " - Storm League - " + rank + " - " + replayId,
                    ReplayId = replayId,
                    Map = map,
                    Rank = rank,
                    PrivacyStatus = "public",
                    CategoryId = "20",
                }
            )
        );

        DateTime now = DateTime.UtcNow;
        ReplayMediaDecision decision = ReplayMediaPolicy.Evaluate(
            new ReplayMediaPolicyInput
            {
                ReplayId = replayId,
                GameDateUtc = now,
                GameVersion = "2.57.0.98304",
                Map = map,
                GameMode = "Storm League",
                Rank = rank,
                Completion = new ReplayMediaCompletion { IsVerifiedComplete = true },
                Media = new ReplayMediaFinalization { IsFinalized = true, IsCorrelated = true },
            },
            settings.ReplayMedia,
            now
        );
        UploadAttemptResult saved = await new UploadOutbox(
            MediaPolicyAttemptLog.AttemptsRoot(settings)
        ).SavePolicyAsync(
            "replay-" + replayId,
            replayId,
            new DateTimeOffset(now),
            MediaPolicyManifest.FromDecision(decision, publicationEvaluated: true),
            replaceOpen: false,
            CancellationToken.None
        );
        Assert.True(saved.Succeeded, saved.Reason);
        return recording;
    }

    private static JsonDocument Receipt(string recording)
    {
        return JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(recording), "youtube-dry-run.json"))
        );
    }
}
