using System;
using System.IO;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Retention;
using Xunit;

namespace HeroesReplay.Tests.Unit.Retention;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MediaRetentionTests
{
    [Fact]
    public void Sweep_RemovesPlayedReplaysAndUploadedRecordings()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-retain-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            string standard = Path.Combine(root, "Standard");
            string requests = Path.Combine(root, "Requests");
            string contexts = Path.Combine(root, "Contexts");
            Directory.CreateDirectory(standard);
            Directory.CreateDirectory(requests);
            Directory.CreateDirectory(Path.Combine(contexts, "10"));
            Directory.CreateDirectory(Path.Combine(contexts, "11"));
            File.WriteAllText(
                Path.Combine(root, "spectated-ids.txt"),
                "10" + Environment.NewLine + "13" + Environment.NewLine
            );
            string oldReplay = Path.Combine(standard, "10_Storm League_Gold_Map_.StormReplay");
            string recentReplay = Path.Combine(standard, "13_Storm League_Gold_Map_.StormReplay");
            File.WriteAllBytes(oldReplay, new byte[32]);
            File.WriteAllBytes(recentReplay, new byte[8]);
            File.WriteAllBytes(
                Path.Combine(standard, "12_Storm League_Gold_Map_.StormReplay"),
                new byte[8]
            );
            File.SetLastWriteTimeUtc(oldReplay, DateTime.UtcNow.AddDays(-40));
            File.SetLastWriteTimeUtc(recentReplay, DateTime.UtcNow.AddDays(-2));
            string uploadedVideo = Path.Combine(contexts, "10", "match.mp4");
            File.WriteAllBytes(uploadedVideo, new byte[64]);
            File.WriteAllText(Path.Combine(contexts, "10", "youtube-entry-uploaded.json"), "{}");
            File.WriteAllBytes(Path.Combine(contexts, "11", "live.mp4"), new byte[16]);
            File.SetLastWriteTimeUtc(uploadedVideo, DateTime.UtcNow.AddDays(-4));
            Directory.SetLastWriteTimeUtc(
                Path.Combine(contexts, "10"),
                DateTime.UtcNow.AddDays(-4)
            );
            Directory.SetLastWriteTimeUtc(Path.Combine(contexts, "11"), DateTime.UtcNow);

            RetentionSweep sweep = MediaRetention.Sweep(Settings(root), DateTimeOffset.UtcNow);

            Assert.False(
                File.Exists(Path.Combine(standard, "10_Storm League_Gold_Map_.StormReplay"))
            );
            Assert.True(
                File.Exists(Path.Combine(standard, "13_Storm League_Gold_Map_.StormReplay"))
            );
            Assert.True(
                File.Exists(Path.Combine(standard, "12_Storm League_Gold_Map_.StormReplay"))
            );
            Assert.False(Directory.Exists(Path.Combine(contexts, "10")));
            Assert.True(File.Exists(Path.Combine(contexts, "11", "live.mp4")));
            Assert.True(sweep.FreedBytes > 0);
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
    public void Sweep_DropsARecordingThatNeverUploaded()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-retain-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            string contexts = Path.Combine(root, "Contexts");
            Directory.CreateDirectory(Path.Combine(contexts, "20"));
            Directory.CreateDirectory(Path.Combine(contexts, "21"));
            File.WriteAllBytes(Path.Combine(contexts, "20", "stuck.mp4"), new byte[40]);
            File.WriteAllBytes(Path.Combine(contexts, "21", "current.mp4"), new byte[4]);
            Directory.SetLastWriteTimeUtc(
                Path.Combine(contexts, "20"),
                DateTime.UtcNow.AddDays(-8)
            );
            Directory.SetLastWriteTimeUtc(Path.Combine(contexts, "21"), DateTime.UtcNow);

            RetentionSweep sweep = MediaRetention.Sweep(Settings(root), DateTimeOffset.UtcNow);

            Assert.False(File.Exists(Path.Combine(contexts, "20", "stuck.mp4")));
            Assert.True(File.Exists(Path.Combine(contexts, "21", "current.mp4")));
            Assert.Contains(sweep.Warnings, warning => warning.Contains("never uploaded"));
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
    public void Sweep_DeletesUploadedMediaOnlyWhenOlderThanVideoKeepDays()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string uploaded = library.AddContext("uploaded");
        string fresh = library.AddContext("fresh");
        string live = library.AddContext("live");
        TempLibrary.WriteText(uploaded, "youtube-entry-uploaded.json", "{}");
        TempLibrary.WriteText(fresh, "youtube-entry-uploaded.json", "{}");
        TempLibrary.WriteFile(uploaded, "young.mp4", 32, now.AddDays(-1));
        TempLibrary.WriteFile(uploaded, "old.mp4", 32, now.AddDays(-4));
        TempLibrary.WriteFile(uploaded, "young.StormReplay", 16, now.AddDays(-1));
        TempLibrary.WriteFile(uploaded, "old.StormReplay", 16, now.AddDays(-4));
        TempLibrary.WriteFile(fresh, "ancient.mp4", 8, now.AddDays(-10));
        TempLibrary.WriteFile(live, "current.mp4", 4, now);
        TempLibrary.SetDirectoryTime(uploaded, now.AddDays(-6));
        TempLibrary.SetDirectoryTime(fresh, now.AddDays(-1));
        TempLibrary.SetDirectoryTime(live, now);

        MediaRetention.Sweep(Settings(library.Root), now);

        Assert.True(File.Exists(Path.Combine(uploaded, "young.mp4")));
        Assert.True(File.Exists(Path.Combine(uploaded, "young.StormReplay")));
        Assert.False(File.Exists(Path.Combine(uploaded, "old.mp4")));
        Assert.False(File.Exists(Path.Combine(uploaded, "old.StormReplay")));
        Assert.True(Directory.Exists(uploaded));
        Assert.False(File.Exists(Path.Combine(fresh, "ancient.mp4")));
        Assert.True(File.Exists(Path.Combine(fresh, "youtube-entry-uploaded.json")));
        Assert.True(File.Exists(Path.Combine(live, "current.mp4")));
    }

    [Fact]
    public void Sweep_DryRunReceiptDoesNotAuthorizeImmediateDeletion()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string dryRun = library.AddContext("dry-run");
        string live = library.AddContext("live");
        TempLibrary.WriteText(dryRun, "youtube-dry-run.json", "{}");
        TempLibrary.WriteFile(dryRun, "young.mp4", 32, now.AddDays(-1));
        TempLibrary.WriteFile(dryRun, "mid.mp4", 32, now.AddDays(-4));
        TempLibrary.WriteFile(dryRun, "old.mp4", 40, now.AddDays(-8));
        TempLibrary.WriteFile(dryRun, "mid.StormReplay", 16, now.AddDays(-4));
        TempLibrary.WriteFile(dryRun, "old.StormReplay", 16, now.AddDays(-8));
        TempLibrary.WriteFile(live, "current.mp4", 4, now);
        TempLibrary.SetDirectoryTime(dryRun, now.AddDays(-8));
        TempLibrary.SetDirectoryTime(live, now);

        RetentionSweep sweep = MediaRetention.Sweep(Settings(library.Root), now);

        Assert.True(File.Exists(Path.Combine(dryRun, "young.mp4")));
        Assert.True(File.Exists(Path.Combine(dryRun, "mid.mp4")));
        Assert.True(File.Exists(Path.Combine(dryRun, "mid.StormReplay")));
        Assert.False(File.Exists(Path.Combine(dryRun, "old.mp4")));
        Assert.False(File.Exists(Path.Combine(dryRun, "old.StormReplay")));
        Assert.Contains(
            sweep.Warnings,
            warning => warning.Contains("never uploaded") && warning.Contains("old.mp4")
        );
        Assert.True(File.Exists(Path.Combine(live, "current.mp4")));
    }

    [Fact]
    public void Sweep_DryRunWithUploadedReceiptIsNotARemoteUpload()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string simulated = library.AddContext("simulated");
        string live = library.AddContext("live");
        TempLibrary.WriteText(simulated, "youtube-dry-run.json", "{}");
        TempLibrary.WriteText(simulated, "youtube-entry-uploaded.json", "{}");
        TempLibrary.WriteFile(simulated, "young.mp4", 32, now.AddDays(-1));
        TempLibrary.WriteFile(simulated, "mid.mp4", 32, now.AddDays(-4));
        TempLibrary.WriteFile(simulated, "old.mp4", 40, now.AddDays(-8));
        TempLibrary.WriteFile(live, "current.mp4", 4, now);
        TempLibrary.SetDirectoryTime(simulated, now.AddDays(-8));
        TempLibrary.SetDirectoryTime(live, now);

        RetentionSweep sweep = MediaRetention.Sweep(Settings(library.Root), now);

        Assert.True(File.Exists(Path.Combine(simulated, "young.mp4")));
        Assert.True(File.Exists(Path.Combine(simulated, "mid.mp4")));
        Assert.False(File.Exists(Path.Combine(simulated, "old.mp4")));
        Assert.Contains(
            sweep.Warnings,
            warning => warning.Contains("never uploaded") && warning.Contains("old.mp4")
        );
    }

    [Fact]
    public void Sweep_ProtectsNewestContextWhenItsMediaIsOlderThanVideoKeepDays()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string current = library.AddContext("current");
        string previous = library.AddContext("previous");
        TempLibrary.WriteText(current, "youtube-entry-uploaded.json", "{}");
        TempLibrary.WriteText(previous, "youtube-entry-uploaded.json", "{}");
        TempLibrary.WriteFile(current, "match.mp4", 64, now.AddDays(-10));
        TempLibrary.WriteFile(current, "match.StormReplay", 16, now.AddDays(-10));
        TempLibrary.WriteFile(previous, "match.mp4", 64, now.AddDays(-10));
        TempLibrary.SetDirectoryTime(current, now);
        TempLibrary.SetDirectoryTime(previous, now.AddDays(-1));

        MediaRetention.Sweep(Settings(library.Root), now);

        Assert.True(File.Exists(Path.Combine(current, "match.mp4")));
        Assert.True(File.Exists(Path.Combine(current, "match.StormReplay")));
        Assert.False(File.Exists(Path.Combine(previous, "match.mp4")));
    }

    [Fact]
    public void Sweep_ProtectsContextThatStillHasYouTubeEntry()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string active = library.AddContext("active");
        string reused = library.AddContext("reused");
        string finished = library.AddContext("finished");
        string abandoned = library.AddContext("abandoned");
        string live = library.AddContext("live");
        TempLibrary.WriteText(active, "youtube-entry.json", "{}");
        TempLibrary.WriteText(reused, "youtube-entry.json", "{}");
        TempLibrary.WriteText(reused, "youtube-entry-uploaded.json", "{}");
        TempLibrary.WriteText(finished, "youtube-entry-uploaded.json", "{}");
        TempLibrary.WriteFile(active, "match.mp4", 32, now.AddDays(-10));
        TempLibrary.WriteFile(reused, "match.mp4", 32, now.AddDays(-10));
        TempLibrary.WriteFile(finished, "match.mp4", 32, now.AddDays(-10));
        TempLibrary.WriteFile(abandoned, "match.mp4", 32, now.AddDays(-10));
        TempLibrary.WriteFile(live, "current.mp4", 4, now);
        TempLibrary.SetDirectoryTime(active, now.AddDays(-10));
        TempLibrary.SetDirectoryTime(reused, now.AddDays(-10));
        TempLibrary.SetDirectoryTime(finished, now.AddDays(-10));
        TempLibrary.SetDirectoryTime(abandoned, now.AddDays(-10));
        TempLibrary.SetDirectoryTime(live, now);

        RetentionSweep sweep = MediaRetention.Sweep(Settings(library.Root), now);

        Assert.True(File.Exists(Path.Combine(active, "match.mp4")));
        Assert.True(File.Exists(Path.Combine(reused, "match.mp4")));
        Assert.False(File.Exists(Path.Combine(finished, "match.mp4")));
        Assert.False(File.Exists(Path.Combine(abandoned, "match.mp4")));
        Assert.Contains(
            sweep.Warnings,
            warning => warning.Contains("never uploaded") && warning.Contains("abandoned")
        );
        Assert.DoesNotContain(sweep.Warnings, warning => warning.Contains("active"));
    }

    [Fact]
    public void Sweep_DeletesTheRecordingOfAnInsertedVideoAwaitingPublishAt()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string inserted = library.AddContext("inserted");
        string waiting = library.AddContext("waiting");
        string live = library.AddContext("live");
        TempLibrary.WriteText(inserted, "youtube-entry.json", "{\"VideoId\":\"abc\"}");
        TempLibrary.WriteText(waiting, "youtube-entry.json", "{\"VideoId\":null}");
        TempLibrary.WriteFile(inserted, "match.mp4", 32, now.AddHours(-1));
        TempLibrary.WriteFile(waiting, "match.mp4", 32, now.AddHours(-1));
        TempLibrary.WriteFile(live, "current.mp4", 4, now);
        TempLibrary.SetDirectoryTime(inserted, now.AddHours(-1));
        TempLibrary.SetDirectoryTime(waiting, now.AddHours(-1));
        TempLibrary.SetDirectoryTime(live, now);

        MediaRetention.Sweep(Settings(library.Root), now);

        Assert.False(File.Exists(Path.Combine(inserted, "match.mp4")));
        Assert.True(File.Exists(Path.Combine(inserted, "youtube-entry.json")));
        Assert.True(File.Exists(Path.Combine(waiting, "match.mp4")));
        Assert.True(File.Exists(Path.Combine(live, "current.mp4")));
    }

    [Fact]
    public void Sweep_KeepsContextReplayNewerThanVideoKeepDays()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string replayOnly = library.AddContext("replay-only");
        string live = library.AddContext("live");
        TempLibrary.WriteFile(replayOnly, "young.StormReplay", 16, now.AddDays(-1));
        TempLibrary.WriteFile(replayOnly, "old.StormReplay", 16, now.AddDays(-4));
        TempLibrary.WriteFile(live, "current.mp4", 4, now);
        TempLibrary.SetDirectoryTime(replayOnly, now.AddDays(-10));
        TempLibrary.SetDirectoryTime(live, now);

        MediaRetention.Sweep(Settings(library.Root), now);

        Assert.True(File.Exists(Path.Combine(replayOnly, "young.StormReplay")));
        Assert.False(File.Exists(Path.Combine(replayOnly, "old.StormReplay")));
        Assert.True(Directory.Exists(replayOnly));
    }

    private static DateTimeOffset FixedNow() => new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private sealed class TempLibrary : IDisposable
    {
        public TempLibrary()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "heroesreplay-retain-" + Guid.NewGuid().ToString("N")
            );
            Contexts = Path.Combine(Root, "Contexts");
            Directory.CreateDirectory(Contexts);
        }

        public string Root { get; }

        public string Contexts { get; }

        public string AddContext(string name)
        {
            string path = Path.Combine(Contexts, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public static void WriteFile(
            string directory,
            string name,
            int bytes,
            DateTimeOffset written
        )
        {
            string path = Path.Combine(directory, name);
            File.WriteAllBytes(path, new byte[bytes]);
            File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        }

        public static void WriteText(string directory, string name, string text)
        {
            File.WriteAllText(Path.Combine(directory, name), text);
        }

        public static void SetDirectoryTime(string directory, DateTimeOffset written)
        {
            Directory.SetLastWriteTimeUtc(directory, written.UtcDateTime);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private static AppSettings Settings(string root) =>
        new()
        {
            Location = new LocationSettings { DataDirectory = root },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
                RequestsCacheDirectoryName = "Requests",
            },
            StormReplay = new StormReplaySettings { Seperator = "_" },
            Retention = new RetentionSettings
            {
                Enabled = true,
                VideoKeepDays = 3,
                VideoMaxAgeDays = 7,
                ReplayKeepDays = 30,
            },
        };
}
