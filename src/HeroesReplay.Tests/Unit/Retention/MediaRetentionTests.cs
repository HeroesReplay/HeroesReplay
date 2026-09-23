using System;
using System.IO;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Retention;
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
            File.WriteAllBytes(Path.Combine(contexts, "10", "match.mp4"), new byte[64]);
            File.WriteAllText(Path.Combine(contexts, "10", "youtube-entry-uploaded.json"), "{}");
            File.WriteAllBytes(Path.Combine(contexts, "11", "live.mp4"), new byte[16]);
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
