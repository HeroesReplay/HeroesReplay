using System;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.YouTube.Outbox;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Outbox;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class UploadAttemptCleanupTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Old = Now.AddDays(-20);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-attempts-" + Guid.NewGuid().ToString("N")
    );

    public UploadAttemptCleanupTests() => Directory.CreateDirectory(Attempts);

    public void Dispose() => TestTemp.Delete(root);

    private string Attempts => Path.Combine(root, "upload-attempts");
    private string Contexts => Path.Combine(root, "Contexts");

    /// <summary>
    /// #492: 1,567 attempt folders on the stream PC, 1,251 of them Prepared with Record=false.
    /// Terminal ones go after AttemptKeepDays; one that is sending, may have sent, or still has
    /// its recording never does.
    /// </summary>
    [Fact]
    public void Plan_RemovesOnlyOldTerminalAttempts()
    {
        string gone = Path.Combine(root, "gone.mp4");
        string onDisk = Path.Combine(root, "kept.mp4");
        File.WriteAllBytes(onDisk, new byte[4]);
        UploadAttemptManifest uploaded = Uploaded("replay-1", gone, Old);
        Write("replay-1", uploaded);
        Write("replay-2", Uploaded("replay-2", onDisk, Old));
        Write("replay-3", Prepared("replay-3", Old, record: false));
        Write("replay-4", Prepared("replay-4", Now.AddDays(-2), record: false));
        Write("replay-5", Prepared("replay-5", Old, record: true));
        Write("replay-6", Pending("replay-6", gone, Old));
        Write("replay-7", Sending("replay-7", gone, Old));
        Write("replay-8", Ambiguous("replay-8", gone, Old));
        // A recording that still waits: its prepared manifest carries the media decision.
        Write("replay-10", Prepared("replay-10", Old, record: true, replayId: 65560010));
        Directory.CreateDirectory(Path.Combine(Contexts, "65560010"));
        File.WriteAllBytes(Path.Combine(Contexts, "65560010", "match.mp4"), new byte[4]);
        Directory.CreateDirectory(Path.Combine(Attempts, "replay-9"));
        File.WriteAllText(
            Path.Combine(Attempts, "replay-9", UploadAttemptStore.ManifestFileName),
            "{"
        );
        File.WriteAllText(Path.Combine(Attempts, "replay-1", UploadAttemptStore.LockFileName), "");

        PrunePlan plan = UploadAttemptCleanup.Plan(Attempts, Contexts, keepDays: 14, Now);

        Assert.Equal(
            new[] { "replay-1", "replay-3", "replay-5" },
            plan.Items.Select(item => Path.GetFileName(item.Path)).Order()
        );
        Assert.All(plan.Items, item => Assert.True(item.IsDirectory));
        Assert.Contains(
            "never recorded",
            plan.Items.Single(item => item.Path.EndsWith("replay-3")).Reason
        );

        plan.DeleteAll();

        Assert.False(Directory.Exists(Path.Combine(Attempts, "replay-1")));
        Assert.True(Directory.Exists(Path.Combine(Attempts, "replay-6")));
        Assert.True(Directory.Exists(Path.Combine(Attempts, "replay-9")));
    }

    private void Write(string attemptId, UploadAttemptManifest manifest)
    {
        Assert.NotNull(manifest);
        string folder = Path.Combine(Attempts, attemptId);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, UploadAttemptStore.ManifestFileName),
            UploadAttemptManifestCodec.Write(manifest)
        );
        string json = File.ReadAllText(Path.Combine(folder, UploadAttemptStore.ManifestFileName));
        UploadAttemptResult read = UploadAttemptManifestCodec.Read(json, attemptId);
        Assert.True(read.Succeeded, $"{attemptId} did not read back ({read.Reason}): {json}");
    }

    /// <summary>A Prepared manifest as the media policy writes one (ASA-SERVER, replay 65550003).</summary>
    private static UploadAttemptManifest Prepared(
        string id,
        DateTimeOffset at,
        bool record,
        int replayId = 65550003
    )
    {
        string json = $$"""
            {
              "Schema": 1,
              "AttemptId": "{{id}}",
              "ReplayId": {{replayId}},
              "State": "Prepared",
              "MediaPath": null,
              "MediaSize": 0,
              "MediaHash": null,
              "VideoId": null,
              "Revision": 2,
              "UpdatedAtUtc": "{{at:O}}",
              "ReceiptKind": null,
              "Policy": {
                "PolicyVersion": "1",
                "ConfigurationVersion": "1",
                "Record": {{(record ? "true" : "false")}},
                "RecordingReason": "{{(record ? "selected" : "expired")}}",
                "PublicationCandidate": false,
                "PublicationReason": "expired",
                "Priority": "Ordinary",
                "RecordingMode": "Selected",
                "PublicationMode": "AllEligible",
                "Score": {
                  "PriorityWeight": 100000,
                  "Recency": 6140,
                  "NotableStrength": 0,
                  "Skill": 0,
                  "Total": 106140,
                  "TieBreakReplayId": 65550003,
                  "TieBreakGameDateTicks": 639262384269578192
                },
                "ExpiresAtUtc": "2026-10-02T00:27:06.9578192Z",
                "EvaluatedAtUtc": "2026-10-08T18:08:06.0335604Z",
                "PublicationEvaluated": true,
                "ReplayId": {{replayId}},
                "GameDateUtc": "2026-09-29T00:27:06.9578192Z",
                "GameVersion": "2.57.0.98285",
                "Map": "Dragon Shire",
                "GameMode": "StormLeague",
                "Rank": null,
                "AverageMmr": null,
                "FocusHero": null
              }
            }
            """;
        return Ok(UploadAttemptManifestCodec.Read(json, id));
    }

    private static UploadAttemptManifest Finalized(string id, string media, DateTimeOffset at)
    {
        UploadAttemptManifest prepared = Ok(UploadAttemptMachine.Prepare(id, 1, at));
        UploadAttemptManifest recording = Ok(UploadAttemptMachine.BeginRecording(prepared, at));
        return Ok(
            UploadAttemptMachine.FinalizeMedia(recording.WithRevision(1), media, 4, "hash", at)
        );
    }

    private static UploadAttemptManifest Pending(string id, string media, DateTimeOffset at) =>
        Ok(UploadAttemptMachine.MarkUploadPending(Finalized(id, media, at).WithRevision(2), at));

    private static UploadAttemptManifest Sending(string id, string media, DateTimeOffset at) =>
        Ok(
            UploadAttemptMachine.Dispatch(
                Pending(id, media, at).WithRevision(3),
                youtubeEnabled: true,
                dryRun: false,
                operatorRetry: false,
                at
            )
        );

    private static UploadAttemptManifest Ambiguous(string id, string media, DateTimeOffset at) =>
        Ok(UploadAttemptMachine.MarkAmbiguous(Sending(id, media, at).WithRevision(4), at));

    private static UploadAttemptManifest Uploaded(string id, string media, DateTimeOffset at) =>
        Ok(
            UploadAttemptMachine.Complete(Sending(id, media, at).WithRevision(4), "video-" + id, at)
        );

    private static UploadAttemptManifest Ok(UploadAttemptResult result)
    {
        Assert.True(result.Succeeded, result.Reason);
        return result.Manifest;
    }
}
