using System;
using System.IO;
using HeroesReplay.Core.Services.YouTube;
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
}
