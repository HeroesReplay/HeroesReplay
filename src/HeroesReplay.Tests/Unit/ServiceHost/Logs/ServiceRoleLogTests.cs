using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using HeroesReplay.Core.ServiceHost.Logs;

namespace HeroesReplay.Tests.Unit.ServiceHost.Logs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceRoleLogTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 23, 59, 0, TimeSpan.Zero);

    [Fact]
    public void Writer_WritesTheRoleDateFileUnderAHeader()
    {
        string root = TempDir();
        try
        {
            var clock = new FakeClock(Start);
            using (var writer = Writer(root, clock, maxBytes: 1024 * 1024))
            {
                writer.Write("first entry");
                writer.Write("second entry");
                Assert.Equal(Path.Combine(root, "download-2026-10-02.log"), writer.CurrentPath);
            }

            string[] lines = File.ReadAllLines(Path.Combine(root, "download-2026-10-02.log"));
            Assert.Equal(3, lines.Length);
            Assert.Equal("2026-10-02T23:59:00.000+00:00 --- download pid 4242 ---", lines[0]);
            Assert.Equal(new[] { "first entry", "second entry" }, lines.Skip(1));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Writer_RollsAtLocalMidnight()
    {
        string root = TempDir();
        try
        {
            var clock = new FakeClock(Start);
            using var writer = Writer(root, clock, maxBytes: 1024 * 1024);
            writer.Write("before midnight");
            clock.Now = Start.AddMinutes(2);
            writer.Write("after midnight");

            Assert.Contains(
                "before midnight",
                ReadWhileOpen(Path.Combine(root, "download-2026-10-02.log"))
            );
            string next = ReadWhileOpen(Path.Combine(root, "download-2026-10-03.log"));
            Assert.Contains("after midnight", next);
            Assert.DoesNotContain("before midnight", next);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Writer_UsesTheClocksLocalZoneForTheDay()
    {
        string root = TempDir();
        try
        {
            // 23:59 UTC is already the next day two hours east.
            var clock = new FakeClock(
                Start,
                TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2")
            );
            using var writer = Writer(root, clock, maxBytes: 1024 * 1024);
            writer.Write("east");

            Assert.Equal(Path.Combine(root, "download-2026-10-03.log"), writer.CurrentPath);
            Assert.StartsWith("2026-10-03T01:59:00.000+02:00", ReadWhileOpen(writer.CurrentPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Writer_RollsToTheNextPartAtTheSizeLimit_AndARestartAppendsWhereThereIsRoom()
    {
        string root = TempDir();
        try
        {
            var clock = new FakeClock(Start.AddHours(-12));
            using (var writer = Writer(root, clock, maxBytes: 200))
            {
                for (int index = 0; index < 10; index++)
                {
                    writer.Write($"entry {index:D2} " + new string('x', 30));
                }
            }

            string[] parts = Directory
                .GetFiles(root)
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ToArray();
            Assert.Equal(
                new[]
                {
                    "download-2026-10-02.1.log",
                    "download-2026-10-02.2.log",
                    "download-2026-10-02.log",
                },
                parts
            );
            Assert.All(
                parts
                    .Take(2)
                    .Append(parts[2])
                    .Select(name => new FileInfo(Path.Combine(root, name))),
                file => Assert.True(file.Length < 400, $"{file.Name} is {file.Length} bytes")
            );

            using (var restarted = Writer(root, clock, maxBytes: 200))
            {
                restarted.Write("after restart");
                Assert.Equal(
                    Path.Combine(root, "download-2026-10-02.2.log"),
                    restarted.CurrentPath
                );
            }

            Assert.Contains(
                "after restart",
                File.ReadAllText(Path.Combine(root, "download-2026-10-02.2.log"))
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Prune_DeletesOldDaysAndExtraFiles_AndLeavesOtherFilesAlone()
    {
        string root = TempDir();
        try
        {
            var today = new DateOnly(2026, 10, 2);
            foreach (
                string name in new[]
                {
                    "download-2026-10-02.log",
                    "download-2026-10-01.log",
                    "download-2026-10-01.1.log",
                    "download-2026-09-19.log",
                    "download-2026-09-18.log",
                    "download-2026-01-01.log",
                    "youtube-2026-01-01.log",
                    "heroesprofile-download.pid",
                    "aspire-dashboard.log",
                    "download-notes.log",
                }
            )
            {
                File.WriteAllText(Path.Combine(root, name), name);
            }

            IReadOnlyList<string> deleted = ServiceRoleLog.Prune(
                root,
                "download",
                today,
                retainedDays: 14,
                maxFiles: 50
            );

            Assert.Equal(
                new[] { "download-2026-01-01.log", "download-2026-09-18.log" },
                deleted.Select(Path.GetFileName).OrderBy(name => name)
            );
            Assert.True(File.Exists(Path.Combine(root, "download-2026-09-19.log")));
            Assert.True(File.Exists(Path.Combine(root, "youtube-2026-01-01.log")));
            Assert.True(File.Exists(Path.Combine(root, "heroesprofile-download.pid")));
            Assert.True(File.Exists(Path.Combine(root, "aspire-dashboard.log")));
            Assert.True(File.Exists(Path.Combine(root, "download-notes.log")));

            ServiceRoleLog.Prune(root, "download", today, retainedDays: 14, maxFiles: 2);
            Assert.Equal(
                new[] { "download-2026-10-01.1.log", "download-2026-10-02.log" },
                ServiceRoleLog
                    .Files(root, "download")
                    .Select(file => Path.GetFileName(file.Path))
                    .OrderBy(name => name)
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Writer_PrunesWhenItOpensAFile()
    {
        string root = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "download-2026-09-01.log"), "old");
            var clock = new FakeClock(Start);
            using var writer = Writer(root, clock, maxBytes: 1024, retainedDays: 14);
            writer.Write("today");

            Assert.False(File.Exists(Path.Combine(root, "download-2026-09-01.log")));
            Assert.True(File.Exists(Path.Combine(root, "download-2026-10-02.log")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LatestPath_IsTheNewestPart_OrTodaysFileWhenThereIsNone()
    {
        string root = TempDir();
        try
        {
            var today = new DateOnly(2026, 10, 2);
            Assert.Equal(
                Path.Combine(root, "twitch-2026-10-02.log"),
                ServiceRoleLog.LatestPath(root, "twitch", today)
            );

            File.WriteAllText(Path.Combine(root, "twitch-2026-09-30.log"), "");
            File.WriteAllText(Path.Combine(root, "twitch-2026-10-01.log"), "");
            File.WriteAllText(Path.Combine(root, "twitch-2026-10-01.3.log"), "");
            File.WriteAllText(Path.Combine(root, "twitch-2026-10-01.12.log"), "");
            Assert.Equal(
                Path.Combine(root, "twitch-2026-10-01.12.log"),
                ServiceRoleLog.LatestPath(root, "twitch", today)
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("download", true)]
    [InlineData("supervisor", true)]
    [InlineData("..\\evil", false)]
    [InlineData("a/b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSafeRole_RejectsPathCharacters(string role, bool expected)
    {
        Assert.Equal(expected, ServiceRoleLog.IsSafeRole(role));
    }

    [Fact]
    public void Provider_FormatsEntriesAndRedactsSecretsInMessagesAndExceptions()
    {
        string root = TempDir();
        try
        {
            var clock = new FakeClock(Start);
            string path;
            using (
                var provider = new ServiceRoleLogProvider(
                    "youtube",
                    new ServiceLogSettings { Directory = root },
                    clock,
                    pid: 77
                )
            )
            {
                ILogger logger = provider.CreateLogger("HeroesReplay.Core.YouTube.YouTubeUploader");
                logger.LogInformation(
                    new EventId(12),
                    "Uploading {File} with access_token={Token}",
                    "a.mp4",
                    "ya29.SECRET"
                );
                logger.LogError(
                    new InvalidOperationException(
                        "GET /x?api_key=HIDDEN failed\nAuthorization: Bearer abc.def"
                    ),
                    "Upload failed"
                );
                path = provider.CurrentPath;
            }

            string text = File.ReadAllText(path);
            string[] lines = File.ReadAllLines(path);
            Assert.StartsWith(
                "2026-10-02T23:59:00.000+00:00 --- youtube pid 77 version ",
                lines[0]
            );
            Assert.Equal(
                "2026-10-02T23:59:00.000+00:00 INF HeroesReplay.Core.YouTube.YouTubeUploader[12]: Uploading a.mp4 with access_token=[redacted]",
                lines[1]
            );
            Assert.Equal(
                "2026-10-02T23:59:00.000+00:00 ERR HeroesReplay.Core.YouTube.YouTubeUploader: Upload failed",
                lines[2]
            );
            Assert.StartsWith(
                "    System.InvalidOperationException: GET /x?api_key=[redacted] failed",
                lines[3]
            );
            Assert.Equal("    Authorization: Bearer [redacted]", lines[4]);
            Assert.DoesNotContain("SECRET", text);
            Assert.DoesNotContain("HIDDEN", text);
            Assert.DoesNotContain("abc.def", text);
            Assert.All(
                lines.Skip(1),
                line => Assert.True(line.StartsWith("2026") || line.StartsWith("    "))
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Provider_TakesItsLevelFromLoggingRoleFile()
    {
        string root = TempDir();
        try
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string>
                    {
                        ["Logging:LogLevel:Default"] = "Debug",
                        ["Logging:RoleFile:LogLevel:Default"] = "Information",
                    }
                )
                .Build();
            string path;
            using (
                var provider = new ServiceRoleLogProvider(
                    "download",
                    new ServiceLogSettings { Directory = root },
                    new FakeClock(Start)
                )
            )
            {
                using (
                    ILoggerFactory factory = LoggerFactory.Create(builder =>
                        builder
                            .AddConfiguration(configuration.GetSection("Logging"))
                            .AddProvider(provider)
                    )
                )
                {
                    ILogger logger = factory.CreateLogger("Download");
                    logger.LogDebug("debug is filtered");
                    logger.LogInformation("information is kept");
                }

                path = provider.CurrentPath;
            }

            string text = File.ReadAllText(path);
            Assert.Contains("information is kept", text);
            Assert.DoesNotContain("debug is filtered", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Settings_DefaultToTwentyMegabytesFourteenDaysAndLocalAppData()
    {
        var settings = new ServiceLogSettings();
        Assert.True(settings.Enabled);
        Assert.Equal(20L * 1024 * 1024, settings.MaxFileBytes);
        Assert.Equal(14, settings.Days);
        Assert.Equal(50, settings.MaxFiles);
        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HeroesReplay",
                "logs"
            ),
            settings.ResolvedDirectory
        );
        Assert.Equal(
            1024L * 1024,
            new ServiceLogSettings { MaxFileSizeMegabytes = 1 }.MaxFileBytes
        );
        Assert.Equal(
            20L * 1024 * 1024,
            new ServiceLogSettings { MaxFileSizeMegabytes = 0 }.MaxFileBytes
        );
    }

    [Fact]
    public void Redaction_IsTheSameForTheLogAndTheHeartbeat()
    {
        const string message = "GET /replays?api_token=s3cret&x=1 oauth:abcdef";
        Assert.Equal(ServiceLogRedaction.Redact(message), ServiceHeartbeat.Redact(message));
        Assert.Equal(
            "GET /replays?api_token=[redacted]&x=1 oauth: [redacted]",
            ServiceLogRedaction.Redact(message)
        );
        Assert.Equal(5000, ServiceLogRedaction.Redact(new string('x', 5000)).Length);
    }

    private static ServiceRoleLogWriter Writer(
        string root,
        TimeProvider clock,
        long maxBytes,
        int retainedDays = 14,
        int maxFiles = 50
    ) => new("download", root, maxBytes, retainedDays, maxFiles, clock, "download pid 4242");

    // A reader that shares the file, as Get-Content does, works while the role is writing.
    private static string ReadWhileOpen(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string TempDir()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-logs-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeClock : TimeProvider
    {
        private readonly TimeZoneInfo zone;

        public FakeClock(DateTimeOffset now, TimeZoneInfo zone = null)
        {
            Now = now;
            this.zone = zone ?? TimeZoneInfo.Utc;
        }

        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => zone;
    }
}
