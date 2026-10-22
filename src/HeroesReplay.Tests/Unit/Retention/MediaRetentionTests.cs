using System;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.YouTube;
using Microsoft.Extensions.Logging.Abstractions;
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
    public void Sweep_DropsAnUnentriedRecordingOnTheNextSweep()
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
            string stuck = Path.Combine(contexts, "20", "stuck.mp4");
            File.WriteAllBytes(stuck, new byte[40]);
            File.SetLastWriteTimeUtc(stuck, DateTime.UtcNow.AddHours(-2));
            File.WriteAllBytes(Path.Combine(contexts, "21", "current.mp4"), new byte[4]);
            Directory.SetLastWriteTimeUtc(
                Path.Combine(contexts, "20"),
                DateTime.UtcNow.AddHours(-2)
            );
            Directory.SetLastWriteTimeUtc(Path.Combine(contexts, "21"), DateTime.UtcNow);

            RetentionSweep sweep = MediaRetention.Sweep(Settings(root), DateTimeOffset.UtcNow);

            Assert.False(File.Exists(stuck));
            Assert.True(File.Exists(Path.Combine(contexts, "21", "current.mp4")));
            Assert.Equal(40, sweep.FreedBytes);
            Assert.Contains(
                sweep.Warnings,
                warning => warning.Contains("never uploaded") && warning.Contains("stuck.mp4")
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

    /// <summary>
    /// #250: retention never deletes a recording that waits for its insert, however old it is
    /// and however much is pending; the spectator's pending-bytes guard stops recording
    /// instead. A recording with no entry goes, and the warning says why.
    /// </summary>
    [Fact]
    public void Sweep_KeepsAnEligibleRecordingAndNamesTheReasonForAnUnentriedOne()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string eligible = library.AddContext("eligible");
        string orphan = library.AddContext("orphan");
        string current = library.AddContext("current");
        TempLibrary.WriteFile(eligible, "match.mp4", 64, now.AddDays(-20));
        TempLibrary.WriteText(eligible, "youtube-entry.json", "{\"ReplayId\":1}");
        TempLibrary.WriteFile(orphan, "match.mp4", 32, now.AddHours(-3));
        TempLibrary.WriteFile(current, "match.mp4", 8, now);
        TempLibrary.SetDirectoryTime(eligible, now.AddDays(-20));
        TempLibrary.SetDirectoryTime(orphan, now.AddHours(-3));
        TempLibrary.SetDirectoryTime(current, now);

        RetentionSweep sweep = MediaRetention.Sweep(Settings(library.Root), now);

        Assert.True(File.Exists(Path.Combine(eligible, "match.mp4")));
        Assert.False(File.Exists(Path.Combine(orphan, "match.mp4")));
        string warning = Assert.Single(sweep.Warnings);
        Assert.Contains("never uploaded", warning, StringComparison.Ordinal);
        Assert.Contains("reason: no-entry", warning, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(orphan, "match.mp4"), warning, StringComparison.Ordinal);
        Assert.Equal(
            64,
            PendingUploadSize.Bytes(
                library.Contexts,
                "youtube-entry.json",
                "youtube-entry-uploaded.json"
            )
        );
        Assert.Equal(
            1,
            PendingUploadSize.Count(
                library.Contexts,
                "youtube-entry.json",
                "youtube-entry-uploaded.json"
            )
        );
    }

    /// <summary>
    /// #212: the uploader touched context A after context B's recording stopped, so A is the
    /// newest folder. B's entry is not written yet. B's recording must survive the sweep.
    /// </summary>
    [Fact]
    public void Sweep_KeepsAJustStoppedRecordingWhenAnotherContextIsNewer()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string stopped = library.AddContext("stopped");
        string uploaded = library.AddContext("uploaded");
        TempLibrary.WriteFile(stopped, "match.mp4", 32, now.AddMinutes(-1));
        TempLibrary.WriteText(uploaded, "youtube-entry.json", "{\"VideoId\":\"abc\"}");
        TempLibrary.SetDirectoryTime(stopped, now.AddMinutes(-1));
        TempLibrary.SetDirectoryTime(uploaded, now);

        RetentionSweep sweep = MediaRetention.Sweep(Settings(library.Root), now);

        Assert.True(File.Exists(Path.Combine(stopped, "match.mp4")));
        Assert.Empty(sweep.Warnings);
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

    /// <summary>#207: a recording another process still has open is retried, not warned about.</summary>
    [Fact]
    public void Sweep_RetriesARecordingStillOpenInAnotherProcess()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string inserted = library.AddContext("inserted");
        string live = library.AddContext("live");
        TempLibrary.WriteText(inserted, "youtube-entry.json", "{\"VideoId\":\"abc\"}");
        TempLibrary.WriteFile(inserted, "match.mp4", 32, now.AddHours(-1));
        TempLibrary.WriteFile(live, "current.mp4", 4, now);
        TempLibrary.SetDirectoryTime(inserted, now.AddHours(-1));
        TempLibrary.SetDirectoryTime(live, now);
        string video = Path.Combine(inserted, "match.mp4");

        RetentionSweep busy;
        using (new FileStream(video, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            busy = MediaRetention.Sweep(Settings(library.Root), now);
        }

        RetentionSweep retried = MediaRetention.Sweep(Settings(library.Root), now);

        Assert.Equal([video], busy.InUse);
        Assert.Empty(busy.Warnings);
        Assert.False(File.Exists(video));
        Assert.Equal(32, retried.FreedBytes);
    }

    [Fact]
    public void Sweep_RemovesAScheduledVideoContextAfterVideoKeepDays()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string scheduled = library.AddContext("scheduled");
        string waiting = library.AddContext("waiting");
        string live = library.AddContext("live");
        TempLibrary.WriteText(scheduled, "youtube-entry.json", "{\"VideoId\":\"abc\"}");
        TempLibrary.WriteText(waiting, "youtube-entry.json", "{\"VideoId\":null}");
        TempLibrary.WriteFile(scheduled, "match.StormReplay", 16, now.AddDays(-5));
        TempLibrary.WriteFile(waiting, "match.mp4", 32, now.AddDays(-5));
        TempLibrary.WriteFile(live, "current.mp4", 4, now);
        TempLibrary.SetDirectoryTime(scheduled, now.AddDays(-5));
        TempLibrary.SetDirectoryTime(waiting, now.AddDays(-5));
        TempLibrary.SetDirectoryTime(live, now);

        MediaRetention.Sweep(Settings(library.Root), now);

        Assert.False(Directory.Exists(scheduled));
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

    /// <summary>
    /// #487: the plan names what the sweep removes, with a reason, and removes nothing itself.
    /// </summary>
    [Fact]
    public void Plan_ListsWhatTheSweepRemoves_WithAReason_AndRemovesNothing()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string uploaded = library.AddContext("uploaded");
        string orphan = library.AddContext("orphan");
        string old = library.AddContext("old");
        string live = library.AddContext("live");
        TempLibrary.WriteText(uploaded, "youtube-entry.json", "{\"VideoId\":\"abc\"}");
        TempLibrary.WriteFile(uploaded, "match.mp4", 64, now.AddHours(-2));
        TempLibrary.WriteFile(orphan, "match.mp4", 32, now.AddHours(-3));
        TempLibrary.WriteText(old, "youtube-entry-uploaded.json", "{}");
        TempLibrary.WriteFile(live, "current.mp4", 4, now);
        TempLibrary.SetDirectoryTime(uploaded, now.AddHours(-2));
        TempLibrary.SetDirectoryTime(orphan, now.AddHours(-3));
        TempLibrary.SetDirectoryTime(old, now.AddDays(-5));
        TempLibrary.SetDirectoryTime(live, now);

        PrunePlan plan = MediaRetention.Plan(Settings(library.Root), now);

        Assert.True(File.Exists(Path.Combine(uploaded, "match.mp4")));
        Assert.True(File.Exists(Path.Combine(orphan, "match.mp4")));
        Assert.True(Directory.Exists(old));
        Assert.Equal(MediaRetention.PrunerName, plan.Pruner);
        Assert.Collection(
            plan.Items.OrderBy(item => item.Path, StringComparer.Ordinal),
            item =>
            {
                Assert.Equal(old, item.Path);
                Assert.True(item.IsDirectory);
                Assert.Contains("VideoKeepDays", item.Reason);
            },
            item =>
            {
                Assert.Equal(Path.Combine(orphan, "match.mp4"), item.Path);
                Assert.Contains("no youtube-entry.json", item.Reason);
                Assert.Contains("reason: no-entry", item.Warning);
            },
            item =>
            {
                Assert.Equal(Path.Combine(uploaded, "match.mp4"), item.Path);
                Assert.Equal(64, item.Bytes);
                Assert.Contains("on YouTube", item.Reason);
            }
        );

        RetentionSweep sweep = MediaRetention.Pruner(Settings(library.Root)).Apply(plan);

        Assert.Equal(3, sweep.DeletedFiles);
        Assert.False(File.Exists(Path.Combine(uploaded, "match.mp4")));
        Assert.False(Directory.Exists(old));
        Assert.True(File.Exists(Path.Combine(live, "current.mp4")));
    }

    [Fact]
    public void SweepAndLog_UnderHousekeepingDryRun_RemovesNothing()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string orphan = library.AddContext("orphan");
        string live = library.AddContext("live");
        TempLibrary.WriteFile(orphan, "match.mp4", 32, now.AddHours(-3));
        TempLibrary.SetDirectoryTime(orphan, now.AddHours(-3));
        TempLibrary.SetDirectoryTime(live, now);
        AppSettings settings = Settings(library.Root);
        settings.Housekeeping = new HousekeepingSettings { DryRun = true };

        MediaRetention.SweepAndLog(settings, NullLogger.Instance);

        Assert.True(File.Exists(Path.Combine(orphan, "match.mp4")));
    }

    /// <summary>
    /// #489, the stream PC's 140 orphans: an entry with no video id whose recording is gone (a
    /// stale or Terminal delete before the entry was marked expired) was protected forever. It
    /// goes once untouched for VideoKeepDays; a younger one, and one whose recording still waits,
    /// stay.
    /// </summary>
    [Fact]
    public void Sweep_RemovesAnEntryWithNoVideoIdAndNoRecording_AfterVideoKeepDays()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string orphan = library.AddContext("orphan");
        string young = library.AddContext("young");
        string waiting = library.AddContext("waiting");
        string live = library.AddContext("live");
        foreach (string folder in new[] { orphan, young, waiting })
        {
            TempLibrary.WriteText(folder, "youtube-entry.json", "{\"ReplayId\":1}");
            TempLibrary.WriteFile(folder, "1_x.StormReplay", 16, now.AddDays(-10));
            TempLibrary.WriteFile(folder, "end.png", 8, now.AddDays(-10));
        }

        File.SetLastWriteTimeUtc(
            Path.Combine(orphan, "youtube-entry.json"),
            now.AddDays(-4).UtcDateTime
        );
        File.SetLastWriteTimeUtc(
            Path.Combine(young, "youtube-entry.json"),
            now.AddDays(-1).UtcDateTime
        );
        TempLibrary.WriteFile(waiting, "match.mp4", 32, now.AddDays(-10));
        TempLibrary.WriteFile(live, "current.mp4", 4, now);
        TempLibrary.SetDirectoryTime(orphan, now.AddDays(-4));
        TempLibrary.SetDirectoryTime(young, now.AddDays(-4));
        TempLibrary.SetDirectoryTime(waiting, now.AddDays(-10));
        TempLibrary.SetDirectoryTime(live, now);

        PrunePlan plan = MediaRetention.Plan(Settings(library.Root), now);
        MediaRetention.Sweep(Settings(library.Root), now);

        PruneItem item = Assert.Single(plan.Items);
        Assert.Equal(orphan, item.Path);
        Assert.Contains("no video id and no recording", item.Reason);
        Assert.False(Directory.Exists(orphan));
        Assert.True(Directory.Exists(young));
        Assert.True(File.Exists(Path.Combine(waiting, "match.mp4")));
    }

    /// <summary>#489: a folder with only the replay and screenshots, no entry and no recording.</summary>
    [Fact]
    public void Sweep_RemovesAReplayAndScreenshotsOnlyFolder_AfterVideoKeepDays()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string leftover = library.AddContext("leftover");
        string live = library.AddContext("live");
        TempLibrary.WriteFile(leftover, "1_x.StormReplay", 16, now.AddDays(-5));
        TempLibrary.WriteFile(leftover, "end.png", 8, now.AddDays(-5));
        TempLibrary.WriteFile(leftover, "end-12-00-00.png", 8, now.AddDays(-5));
        TempLibrary.WriteFile(live, "current.mp4", 4, now);
        TempLibrary.SetDirectoryTime(leftover, now.AddDays(-5));
        TempLibrary.SetDirectoryTime(live, now);

        MediaRetention.Sweep(Settings(library.Root), now);

        Assert.False(Directory.Exists(leftover));
    }

    /// <summary>
    /// #489: VideoMaxAgeDays bounds a dry run's context while YouTube:DryRun is on, even with its
    /// entry still waiting: first the recording, then the folder. With DryRun off a live uploader
    /// would send it, so it waits like any other recording.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sweep_DryRunContexts_GoAfterVideoMaxAgeDays_OnlyWhileDryRunIsOn(bool dryRun)
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string planned = library.AddContext("planned");
        string emptied = library.AddContext("emptied");
        string live = library.AddContext("live");
        foreach (string folder in new[] { planned, emptied })
        {
            TempLibrary.WriteText(folder, "youtube-entry.json", "{\"ReplayId\":1}");
            TempLibrary.WriteText(folder, DryRunRecordings.PlanFileName, "{}");
        }

        TempLibrary.WriteFile(planned, "match.mp4", 32, now.AddDays(-8));
        TempLibrary.WriteFile(emptied, "end.png", 8, now.AddDays(-8));
        TempLibrary.WriteFile(live, "current.mp4", 4, now);
        TempLibrary.SetDirectoryTime(planned, now.AddDays(-8));
        TempLibrary.SetDirectoryTime(emptied, now.AddDays(-8));
        TempLibrary.SetDirectoryTime(live, now);
        AppSettings settings = Settings(library.Root);
        settings.YouTube = new YouTubeSettings { DryRun = dryRun };

        RetentionSweep sweep = MediaRetention.Sweep(settings, now);

        Assert.Equal(!dryRun, File.Exists(Path.Combine(planned, "match.mp4")));
        Assert.Equal(!dryRun, Directory.Exists(emptied));
        if (dryRun)
        {
            Assert.Contains(sweep.Warnings, warning => warning.Contains("reason: dry-run"));
        }
    }

    /// <summary>
    /// #489: a recording the uploader or the pending-bytes gate retired leaves an expired entry.
    /// A recording its delete missed goes on the next sweep, and the folder ages out
    /// VideoKeepDays after the rename.
    /// </summary>
    [Fact]
    public void Sweep_ARetiredRecordingsFolder_AgesOutLikeAnUpload()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string missed = library.AddContext("missed");
        string retired = library.AddContext("retired");
        string live = library.AddContext("live");
        TempLibrary.WriteText(missed, RetiredRecordings.EntryFileName, "{}");
        TempLibrary.WriteFile(missed, "match.mp4", 32, now.AddDays(-1));
        TempLibrary.WriteText(retired, RetiredRecordings.EntryFileName, "{}");
        TempLibrary.WriteFile(retired, "end.png", 8, now.AddDays(-6));
        TempLibrary.WriteFile(live, "current.mp4", 4, now);
        TempLibrary.SetDirectoryTime(missed, now.AddHours(-2));
        TempLibrary.SetDirectoryTime(retired, now.AddDays(-4));
        TempLibrary.SetDirectoryTime(live, now);

        RetentionSweep sweep = MediaRetention.Sweep(Settings(library.Root), now);

        Assert.False(File.Exists(Path.Combine(missed, "match.mp4")));
        Assert.True(Directory.Exists(missed));
        Assert.False(Directory.Exists(retired));
        Assert.DoesNotContain(sweep.Warnings, warning => warning.Contains("no-entry"));
    }

    [Fact]
    public void MarkExpired_RenamesTheEntry_SoTheFolderNoLongerWaits()
    {
        using TempLibrary library = new TempLibrary();
        string context = library.AddContext("stale");
        TempLibrary.WriteText(context, "youtube-entry.json", "{\"ReplayId\":1}");
        string recording = Path.Combine(context, "match.mp4");

        Assert.True(RetiredRecordings.MarkExpired(recording, "youtube-entry.json"));
        Assert.False(File.Exists(Path.Combine(context, "youtube-entry.json")));
        Assert.Equal(
            "{\"ReplayId\":1}",
            File.ReadAllText(Path.Combine(context, RetiredRecordings.EntryFileName))
        );
        Assert.False(RetiredRecordings.MarkExpired(recording, "youtube-entry.json"));
    }

    /// <summary>
    /// #489: OBS records into the Contexts root when SetRecordDirectory failed. Nothing can upload
    /// that file; it goes once quiet, and one OBS may still write stays.
    /// </summary>
    [Fact]
    public void Sweep_RemovesAQuietRecordingInTheContextsRoot()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        TempLibrary.WriteFile(library.Contexts, "2026-10-20 12-00-00.mp4", 32, now.AddHours(-3));
        TempLibrary.WriteFile(library.Contexts, "2026-10-22 11-50-00.mp4", 32, now.AddMinutes(-5));

        RetentionSweep sweep = MediaRetention.Sweep(Settings(library.Root), now);

        Assert.False(File.Exists(Path.Combine(library.Contexts, "2026-10-20 12-00-00.mp4")));
        Assert.True(File.Exists(Path.Combine(library.Contexts, "2026-10-22 11-50-00.mp4")));
        Assert.Contains(sweep.Warnings, warning => warning.Contains("reason: contexts-root"));
    }

    /// <summary>
    /// #491: once the video is on YouTube the end capture has no further use, so it goes with the
    /// mp4 (the legacy end-HH-MM-SS.png copy too), not days later with the folder.
    /// </summary>
    [Fact]
    public void Sweep_DropsTheEndCaptureWithAnUploadedRecording()
    {
        using TempLibrary library = new TempLibrary();
        DateTimeOffset now = FixedNow();
        string uploaded = library.AddContext("uploaded");
        string waiting = library.AddContext("waiting");
        string live = library.AddContext("live");
        TempLibrary.WriteText(uploaded, "youtube-entry.json", "{\"VideoId\":\"abc\"}");
        TempLibrary.WriteText(waiting, "youtube-entry.json", "{\"ReplayId\":2}");
        foreach (string folder in new[] { uploaded, waiting })
        {
            TempLibrary.WriteFile(folder, "match.mp4", 32, now.AddHours(-2));
            TempLibrary.WriteFile(folder, "end.png", 8, now.AddHours(-2));
            TempLibrary.WriteFile(folder, "end-00-21-14.png", 8, now.AddHours(-2));
            TempLibrary.WriteFile(folder, "2_x.StormReplay", 16, now.AddHours(-2));
        }

        TempLibrary.WriteFile(live, "current.mp4", 4, now);
        TempLibrary.SetDirectoryTime(uploaded, now.AddHours(-2));
        TempLibrary.SetDirectoryTime(waiting, now.AddHours(-2));
        TempLibrary.SetDirectoryTime(live, now);

        MediaRetention.Sweep(Settings(library.Root), now);

        Assert.Equal(
            new[] { "2_x.StormReplay", "youtube-entry.json" },
            Directory.GetFiles(uploaded).Select(Path.GetFileName).Order()
        );
        Assert.True(File.Exists(Path.Combine(waiting, "end.png")));
        Assert.True(File.Exists(Path.Combine(waiting, "match.mp4")));
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
