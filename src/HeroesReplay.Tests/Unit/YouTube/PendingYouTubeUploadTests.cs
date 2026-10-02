using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Outbox;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PendingYouTubeUploadTests
{
    [Fact]
    public void Find_ReturnsAFinishedRecordingThatWasNeverUploaded()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-yt-" + Path.GetRandomFileName());
        string context = Path.Combine(root, "65536854");
        try
        {
            Directory.CreateDirectory(context);
            File.WriteAllText(Path.Combine(context, "match.mp4"), "video");
            File.WriteAllText(Path.Combine(context, "youtube-entry.json"), "{}");

            var found = PendingYouTubeUpload.Find(
                root,
                "youtube-entry.json",
                "youtube-entry-uploaded.json"
            );

            Assert.Single(found);
            Assert.Equal(Path.Combine(context, "match.mp4"), found[0]);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Find_SkipsARecordingThatAlreadyHasAnUploadReceipt()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-yt-" + Path.GetRandomFileName());
        string context = Path.Combine(root, "65536854");
        try
        {
            Directory.CreateDirectory(context);
            File.WriteAllText(Path.Combine(context, "match.mp4"), "video");
            File.WriteAllText(Path.Combine(context, "youtube-entry.json"), "{}");
            File.WriteAllText(Path.Combine(context, "youtube-entry-uploaded.json"), "{}");

            var found = PendingYouTubeUpload.Find(
                root,
                "youtube-entry.json",
                "youtube-entry-uploaded.json"
            );

            Assert.Empty(found);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void RequestsFirst_SendsAViewerRequestBeforeOlderRecordings()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-yt-" + Path.GetRandomFileName());
        try
        {
            string oldest = Recording(root, "1", "{\"Requested\":false}");
            string unreadable = Recording(root, "2", "not json");
            string request = Recording(root, "3", "{\"Requested\":true}");
            string missing = Path.Combine(root, "4", "match.mp4");

            IReadOnlyList<string> ordered = PendingYouTubeUpload.RequestsFirst(
                new[] { oldest, unreadable, request, missing },
                "youtube-entry.json"
            );

            Assert.Equal(new[] { request, oldest, unreadable, missing }, ordered);
            Assert.Empty(PendingYouTubeUpload.RequestsFirst(null, "youtube-entry.json"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void RequestsFirst_ARequestDoesNotWaitBehindFortyEightOlderRecordings()
    {
        // #165/#161: 6 inserts a day, and replay 65625279 sat behind about 48 ordinary ones.
        string root = Path.Combine(Path.GetTempPath(), "hr-yt-" + Path.GetRandomFileName());
        try
        {
            var older = new List<string>();
            for (int i = 0; i < 48; i++)
            {
                older.Add(Recording(root, "6558" + i.ToString("D4"), "{\"Requested\":false}"));
            }

            string request = Recording(root, "65625279", "{\"Requested\":true}");
            var found = new List<string>(older) { request };

            IReadOnlyList<string> ordered = PendingYouTubeUpload.RequestsFirst(
                found,
                "youtube-entry.json"
            );

            Assert.Equal(request, ordered[0]);
            Assert.Equal(older, ordered.Skip(1));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CountRequestsWaiting_CountsRequestsThatStillNeedAnInsert()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-yt-" + Path.GetRandomFileName());
        try
        {
            string ordinary = Recording(root, "1", "{\"Requested\":false}");
            string waiting = Recording(root, "2", "{\"Requested\":true}");
            Recording(root, "3", "{\"Requested\":true,\"VideoId\":\"abc\"}");
            Recording(root, "4", "{\"Requested\":true}");
            File.WriteAllText(Path.Combine(root, "4", "youtube-entry-uploaded.json"), "{}");
            string noVideo = Path.Combine(root, "5");
            Directory.CreateDirectory(noVideo);
            File.WriteAllText(Path.Combine(noVideo, "youtube-entry.json"), "{\"Requested\":true}");

            Assert.Equal(
                1,
                PendingYouTubeUpload.CountRequestsWaiting(
                    root,
                    "youtube-entry.json",
                    "youtube-entry-uploaded.json",
                    ordinary
                )
            );
            Assert.Equal(
                0,
                PendingYouTubeUpload.CountRequestsWaiting(
                    root,
                    "youtube-entry.json",
                    "youtube-entry-uploaded.json",
                    waiting
                )
            );
            Assert.Equal(
                0,
                PendingYouTubeUpload.CountRequestsWaiting(
                    Path.Combine(root, "missing"),
                    "youtube-entry.json",
                    "youtube-entry-uploaded.json",
                    null
                )
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Find_PicksTheNewestRecordingAndReturnsOlderFoldersFirst()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-yt-" + Path.GetRandomFileName());
        string older = Path.Combine(root, "older");
        string newer = Path.Combine(root, "newer");
        try
        {
            Directory.CreateDirectory(older);
            Directory.CreateDirectory(newer);
            string first = Path.Combine(older, "early.mp4");
            string second = Path.Combine(older, "late.mp4");
            string third = Path.Combine(newer, "match.mp4");
            File.WriteAllText(first, "a");
            File.WriteAllText(second, "b");
            File.WriteAllText(third, "c");
            File.WriteAllText(Path.Combine(older, "youtube-entry.json"), "{}");
            File.WriteAllText(Path.Combine(newer, "youtube-entry.json"), "{}");
            File.SetLastWriteTimeUtc(first, new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(second, new DateTime(2026, 9, 28, 1, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(third, new DateTime(2026, 9, 28, 2, 0, 0, DateTimeKind.Utc));

            var found = PendingYouTubeUpload.Find(
                root,
                "youtube-entry.json",
                "youtube-entry-uploaded.json"
            );

            Assert.Equal(2, found.Count);
            Assert.Equal(second, found[0]);
            Assert.Equal(third, found[1]);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Find_UsesTheBoundAttemptManifestInsteadOfTheNewestFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-yt-" + Path.GetRandomFileName());
        string context = Path.Combine(root, "65536854");
        string attempts = Path.Combine(root, "upload-attempts");
        try
        {
            Directory.CreateDirectory(context);
            string early = Path.Combine(context, "early.mp4");
            string late = Path.Combine(context, "late.mp4");
            File.WriteAllText(early, "owned");
            File.WriteAllText(late, "newer");
            File.WriteAllText(Path.Combine(context, "youtube-entry.json"), "{}");
            File.SetLastWriteTimeUtc(early, new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(late, new DateTime(2026, 9, 28, 2, 0, 0, DateTimeKind.Utc));
            WriteManifest(attempts, "replay-bound", BoundManifest(early));

            var found = PendingYouTubeUpload.Find(
                root,
                "youtube-entry.json",
                "youtube-entry-uploaded.json",
                attempts
            );

            Assert.Equal(early, Assert.Single(found));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Find_KeepsTheNewestFileWhenTheManifestIsStillRecording()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-yt-" + Path.GetRandomFileName());
        string context = Path.Combine(root, "65536854");
        string attempts = Path.Combine(root, "upload-attempts");
        try
        {
            Directory.CreateDirectory(context);
            string early = Path.Combine(context, "early.mp4");
            string late = Path.Combine(context, "late.mp4");
            File.WriteAllText(early, "a");
            File.WriteAllText(late, "b");
            File.WriteAllText(Path.Combine(context, "youtube-entry.json"), "{}");
            File.SetLastWriteTimeUtc(early, new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(late, new DateTime(2026, 9, 28, 2, 0, 0, DateTimeKind.Utc));
            DateTimeOffset at = new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero);
            UploadAttemptResult prepared = UploadAttemptMachine.Prepare(
                "replay-open",
                65536854,
                at
            );
            UploadAttemptResult recording = UploadAttemptMachine.BeginRecording(
                prepared.Manifest,
                at.AddSeconds(1)
            );
            WriteManifest(attempts, "replay-open", recording.Manifest.WithRevision(1));

            var found = PendingYouTubeUpload.Find(
                root,
                "youtube-entry.json",
                "youtube-entry-uploaded.json",
                attempts
            );

            Assert.Equal(late, Assert.Single(found));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static UploadAttemptManifest BoundManifest(string mediaPath)
    {
        DateTimeOffset at = new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero);
        UploadAttemptResult prepared = UploadAttemptMachine.Prepare("replay-bound", 65536854, at);
        UploadAttemptResult recording = UploadAttemptMachine.BeginRecording(
            prepared.Manifest,
            at.AddSeconds(1)
        );
        UploadAttemptResult finalized = UploadAttemptMachine.FinalizeMedia(
            recording.Manifest.WithRevision(1),
            mediaPath,
            4,
            "hash-early",
            at.AddSeconds(2)
        );
        return finalized.Manifest;
    }

    private static void WriteManifest(
        string attempts,
        string attemptId,
        UploadAttemptManifest manifest
    )
    {
        string directory = Path.Combine(attempts, attemptId);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, UploadAttemptStore.ManifestFileName),
            UploadAttemptManifestCodec.Write(manifest)
        );
    }

    private static string Recording(string root, string name, string entry)
    {
        string context = Path.Combine(root, name);
        Directory.CreateDirectory(context);
        File.WriteAllText(Path.Combine(context, "youtube-entry.json"), entry);
        string recording = Path.Combine(context, "match.mp4");
        File.WriteAllText(recording, "video");
        return recording;
    }
}
