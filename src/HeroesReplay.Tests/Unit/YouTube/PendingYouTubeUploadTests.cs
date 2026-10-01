using System;
using System.IO;
using HeroesReplay.Core.Services.YouTube;
using HeroesReplay.Core.Services.YouTube.Outbox;
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
}
