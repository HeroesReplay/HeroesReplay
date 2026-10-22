using System;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

/// <summary>
/// #490: a parked recording (retries used, session gone, or already on the channel) kept its mp4,
/// entry and folder forever, unseen, and counted towards the pending-bytes gate.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ParkedRecordingsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-parked-" + Guid.NewGuid().ToString("N")
    );

    public ParkedRecordingsTests() => Directory.CreateDirectory(root);

    public void Dispose() => TestTemp.Delete(root);

    private string Contexts => Path.Combine(root, "Contexts");

    [Fact]
    public void Mark_KeepsTheFirstTimeForAReason_AndClearRemovesIt()
    {
        string recording = Recording(1, requested: false);
        var entry = new YouTubeEntry { ReplayId = 1 };

        ParkedRecordings.Mark(
            recording,
            ParkedRecordings.OperatorRetry,
            "replay-1",
            entry,
            Now.AddDays(-3)
        );
        ParkedRecordings.Mark(recording, ParkedRecordings.OperatorRetry, "replay-1", entry, Now);

        ParkedRecording read = ParkedRecordings.Read(Path.GetDirectoryName(recording));
        Assert.Equal(Now.AddDays(-3), read.Since);
        Assert.Equal("replay-1", read.AttemptId);

        ParkedRecordings.Mark(recording, ParkedRecordings.SessionGone, "replay-1", entry, Now);
        Assert.Equal(Now, ParkedRecordings.Read(Path.GetDirectoryName(recording)).Since);

        ParkedRecordings.Clear(recording);
        Assert.False(ParkedRecordings.IsParked(Path.GetDirectoryName(recording)));
    }

    [Fact]
    public void Plan_RemovesADuplicateAtOnce_AndOtherParkedAfterParkedKeepDays_NeverARequest()
    {
        string duplicate = Park(1, ParkedRecordings.OnChannel, Now.AddHours(-1));
        string young = Park(2, ParkedRecordings.OperatorRetry, Now.AddDays(-2));
        string old = Park(3, ParkedRecordings.SessionGone, Now.AddDays(-8));
        string request = Park(4, ParkedRecordings.OperatorRetry, Now.AddDays(-30), requested: true);
        string sending = Park(5, ParkedRecordings.OperatorRetry, Now.AddDays(-30));
        string attempt = Path.Combine(root, "upload-attempts", "replay-5");
        Directory.CreateDirectory(attempt);
        File.WriteAllText(
            Path.Combine(attempt, "attempt-manifest.json"),
            Core.YouTube.Outbox.UploadAttemptManifestCodec.Write(Sending("replay-5", sending))
        );
        AppSettings settings = Settings();

        PrunePlan plan = ParkedRecordings.Plan(settings, Now);

        Assert.Equal(
            new[] { duplicate, old }.Order(),
            plan.Items.Select(item => item.Path).Order()
        );
        Assert.Contains("duplicate", plan.Items.Single(item => item.Path == duplicate).Reason);
        Assert.Contains(
            "reason: parked-session-gone",
            plan.Items.Single(item => item.Path == old).Warning
        );

        RetentionSweep sweep = ParkedRecordings.Pruner(settings).Apply(plan);

        Assert.Equal(2, sweep.DeletedFiles);
        string oldFolder = Path.GetDirectoryName(old);
        Assert.False(File.Exists(Path.Combine(oldFolder, "youtube-entry.json")));
        Assert.True(File.Exists(Path.Combine(oldFolder, RetiredRecordings.EntryFileName)));
        Assert.False(ParkedRecordings.IsParked(oldFolder));
        Assert.True(File.Exists(young));
        Assert.True(File.Exists(request));
        Assert.True(File.Exists(sending));
    }

    [Fact]
    public void PendingBytes_LeaveOutParkedRecordings()
    {
        Recording(1, requested: false);
        Park(2, ParkedRecordings.OperatorRetry, Now.AddDays(-1));

        Assert.Equal(
            1,
            PendingUploadSize.Count(Contexts, "youtube-entry.json", "youtube-entry-uploaded.json")
        );
    }

    [Fact]
    public void Check_NamesParkedRecordings_AndWarnsForARequestWaitingForAnOperator()
    {
        Park(1, ParkedRecordings.OperatorRetry, Now.AddDays(-2));
        Park(4, ParkedRecordings.SessionGone, Now.AddDays(-9), requested: true);
        Directory.CreateDirectory(Path.Combine(root, "Requests"));
        File.WriteAllText(
            Path.Combine(root, "Requests", "4_QM_Gold_Map_.request.json"),
            "{\"Request\":{\"Login\":\"viewer42\",\"RewardTitle\":\"ReplayId\",\"ReplayId\":4}}"
        );
        var summary = ParkedRecordings.Summarize(Settings());
        var disk = new StorageSummary { DataBytes = 1, FreeBytes = 1 };
        var state = new HousekeepingState
        {
            LastRun = new HousekeepingRun { StartedAt = Now, Outcome = HousekeepingRun.Ok },
        };

        HousekeepingCheckReport report = HousekeepingCheck.Evaluate(
            disk,
            state,
            new HousekeepingSettings(),
            Now,
            summary,
            parkedKeepDays: 7
        );

        Assert.True(report.Warning);
        Assert.Equal(HousekeepingCheck.ParkedRequest, report.Reason);
        Assert.Contains("Parked recordings: 2", report.Detail);
        Assert.Contains("replay 4", report.Detail);
        Assert.Contains("viewer42", report.Detail);
        Assert.Contains("ReplayId", report.Detail);
    }

    private AppSettings Settings() =>
        new()
        {
            Location = new LocationSettings { DataDirectory = root },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
                RequestsCacheDirectoryName = "Requests",
            },
            YouTube = new YouTubeSettings
            {
                EntryFileName = "youtube-entry.json",
                EntryFileNameUploaded = "youtube-entry-uploaded.json",
            },
            Retention = new RetentionSettings { ParkedKeepDays = 7 },
        };

    private string Recording(int replayId, bool requested)
    {
        string folder = Path.Combine(Contexts, replayId.ToString());
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "youtube-entry.json"),
            $"{{\"ReplayId\":{replayId},\"Requested\":{(requested ? "true" : "false")}}}"
        );
        string recording = Path.Combine(folder, "match.mp4");
        File.WriteAllBytes(recording, new byte[64]);
        return recording;
    }

    private string Park(int replayId, string reason, DateTimeOffset since, bool requested = false)
    {
        string recording = Recording(replayId, requested);
        ParkedRecordings.Mark(
            recording,
            reason,
            "replay-" + replayId,
            new YouTubeEntry { ReplayId = replayId, Requested = requested },
            since
        );
        return recording;
    }

    private static Core.YouTube.Outbox.UploadAttemptManifest Sending(string id, string media)
    {
        var at = Now.AddDays(-30);
        var prepared = Core.YouTube.Outbox.UploadAttemptMachine.Prepare(id, 5, at).Manifest;
        var recording = Core
            .YouTube.Outbox.UploadAttemptMachine.BeginRecording(prepared, at)
            .Manifest;
        var finalized = Core
            .YouTube.Outbox.UploadAttemptMachine.FinalizeMedia(
                recording.WithRevision(1),
                media,
                64,
                "hash",
                at
            )
            .Manifest;
        var pending = Core
            .YouTube.Outbox.UploadAttemptMachine.MarkUploadPending(finalized.WithRevision(2), at)
            .Manifest;
        return Core
            .YouTube.Outbox.UploadAttemptMachine.Dispatch(
                pending.WithRevision(3),
                true,
                false,
                false,
                at
            )
            .Manifest;
    }
}
