using System;
using System.IO;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.YouTube;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HeroesReplay.Tests.Unit.Retention;

/// <summary>
/// #317: a dry run never sends a recording and its entry stays youtube-entry.json, so the
/// ordinary sweep keeps it for good. Once the dry-run plan is written and the mp4 is older than
/// Retention:DryRunRecordingMaxAge, the mp4 goes; clips, end.png, and the json files stay.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class DryRunRecordingsTests : IDisposable
{
    private const int RecordingBytes = 64;
    private const string Entry = "youtube-entry.json";
    private const string Uploaded = "youtube-entry-uploaded.json";
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(2);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-dry-run-sweep-" + Guid.NewGuid().ToString("N")
    );

    public DryRunRecordingsTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Sweep_RemovesAnOldPlannedRecordingAndKeepsItsClipsEndPngAndJson()
    {
        AppSettings settings = Settings(dryRun: true, MaxAge);
        string context = Stage("65581722", recordedDaysAgo: 3, plannedDaysAgo: 2.5);
        string clip = Path.Combine(context, "clips", "pentakill-1", "clip.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(clip));
        File.WriteAllBytes(clip, new byte[8]);
        File.SetLastWriteTimeUtc(clip, Now.AddDays(-3).UtcDateTime);
        foreach (
            string kept in new[] { "end.png", "clips.json", "OBS.txt", "65581722_.StormReplay" }
        )
        {
            File.WriteAllText(Path.Combine(context, kept), "x");
            File.SetLastWriteTimeUtc(Path.Combine(context, kept), Now.AddDays(-3).UtcDateTime);
        }

        RetentionSweep sweep = DryRunRecordings.Sweep(settings, Now);

        Assert.False(File.Exists(Path.Combine(context, "match.mp4")));
        Assert.True(File.Exists(clip));
        Assert.True(File.Exists(Path.Combine(context, "end.png")));
        Assert.True(File.Exists(Path.Combine(context, "clips.json")));
        Assert.True(File.Exists(Path.Combine(context, "OBS.txt")));
        Assert.True(File.Exists(Path.Combine(context, "65581722_.StormReplay")));
        Assert.True(File.Exists(Path.Combine(context, "youtube-entry.json")));
        Assert.True(File.Exists(Path.Combine(context, DryRunRecordings.PlanFileName)));
        Assert.Equal(1, sweep.DeletedFiles);
        Assert.Equal(RecordingBytes, sweep.FreedBytes);
        Assert.Empty(sweep.Warnings);
        Assert.Equal(0, PendingUploadSize.Count(settings.ContextsDirectory, Entry, Uploaded));
    }

    /// <summary>The age is the recording's own: one just under the limit stays.</summary>
    [Theory]
    [InlineData(47, true)]
    [InlineData(49, false)]
    public void Sweep_KeepsARecordingYoungerThanTheMaxAge(int recordedHoursAgo, bool kept)
    {
        AppSettings settings = Settings(dryRun: true, MaxAge);
        string context = Stage(
            "1",
            recordedDaysAgo: recordedHoursAgo / 24.0,
            plannedDaysAgo: (recordedHoursAgo - 1) / 24.0
        );

        RetentionSweep sweep = DryRunRecordings.Sweep(settings, Now);

        Assert.Equal(kept, File.Exists(Path.Combine(context, "match.mp4")));
        Assert.Equal(kept ? 0 : 1, sweep.DeletedFiles);
    }

    /// <summary>
    /// Only a recording the plan was written after goes. A context without a plan, and a newer
    /// recording of the same replay that has no plan of its own yet, stay.
    /// </summary>
    [Fact]
    public void Sweep_KeepsARecordingWithoutAPlanAndOneRecordedAfterThePlan()
    {
        AppSettings settings = Settings(dryRun: true, MaxAge);
        string unplanned = Stage("1", recordedDaysAgo: 5, plannedDaysAgo: null);
        string replayed = Stage("2", recordedDaysAgo: 6, plannedDaysAgo: 5.5);
        string again = Path.Combine(replayed, "again.mp4");
        File.WriteAllBytes(again, new byte[RecordingBytes]);
        File.SetLastWriteTimeUtc(again, Now.AddDays(-4).UtcDateTime);

        RetentionSweep sweep = DryRunRecordings.Sweep(settings, Now);

        Assert.True(File.Exists(Path.Combine(unplanned, "match.mp4")));
        Assert.False(File.Exists(Path.Combine(replayed, "match.mp4")));
        Assert.True(File.Exists(again));
        Assert.Equal(1, sweep.DeletedFiles);
    }

    /// <summary>
    /// Production is unchanged: with DryRun off (or YouTube settings missing) the sweep removes
    /// nothing, even with a plan left by an earlier dry run and an age set.
    /// </summary>
    [Fact]
    public void Sweep_IsOffWhenDryRunIsFalse()
    {
        AppSettings settings = Settings(dryRun: false, MaxAge);
        string context = Stage("1", recordedDaysAgo: 10, plannedDaysAgo: 9);

        RetentionSweep live = DryRunRecordings.Sweep(settings, Now);
        settings.YouTube = null;
        RetentionSweep unset = DryRunRecordings.Sweep(settings, Now);

        Assert.True(File.Exists(Path.Combine(context, "match.mp4")));
        Assert.Equal(0, live.DeletedFiles);
        Assert.Equal(0, unset.DeletedFiles);
        Assert.Empty(live.Warnings);
    }

    /// <summary>A zero age (the base and production setting) and retention off remove nothing.</summary>
    [Fact]
    public void Sweep_IsOffWithAZeroAgeOrRetentionOff()
    {
        AppSettings zero = Settings(dryRun: true, TimeSpan.Zero);
        AppSettings disabled = Settings(dryRun: true, MaxAge);
        disabled.Retention.Enabled = false;
        string context = Stage("1", recordedDaysAgo: 10, plannedDaysAgo: 9);

        Assert.Equal(0, DryRunRecordings.Sweep(zero, Now).DeletedFiles);
        Assert.Equal(0, DryRunRecordings.Sweep(disabled, Now).DeletedFiles);
        Assert.Equal(0, DryRunRecordings.Sweep(new AppSettings(), Now).DeletedFiles);
        Assert.True(File.Exists(Path.Combine(context, "match.mp4")));
    }

    /// <summary>Dev sweeps after 2 days. The base file and production leave it off.</summary>
    [Fact]
    public void Settings_DevUsesTwoDaysAndBaseAndProductionAreOff()
    {
        string basePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        string devPath = Path.Combine(AppContext.BaseDirectory, "appsettings.dev.json");
        string prodPath = FindRepoFile(
            Path.Combine("src", "HeroesReplay.CLI", "appsettings.prod.json")
        );

        Assert.Equal(TimeSpan.FromDays(2), MaxAgeFrom(basePath, devPath));
        Assert.Equal(TimeSpan.Zero, MaxAgeFrom(basePath));
        Assert.Equal(TimeSpan.Zero, MaxAgeFrom(basePath, prodPath));
        Assert.Equal(TimeSpan.Zero, new RetentionSettings().DryRunRecordingMaxAge);
    }

    private AppSettings Settings(bool dryRun, TimeSpan maxAge) =>
        new()
        {
            Location = new LocationSettings { DataDirectory = root },
            Retention = new RetentionSettings { Enabled = true, DryRunRecordingMaxAge = maxAge },
            YouTube = new YouTubeSettings
            {
                Enabled = true,
                DryRun = dryRun,
                EntryFileName = Entry,
                EntryFileNameUploaded = Uploaded,
            },
        };

    /// <summary>
    /// A finished recording with its entry, and the dry-run plan when
    /// <paramref name="plannedDaysAgo"/> is set.
    /// </summary>
    private string Stage(string id, double recordedDaysAgo, double? plannedDaysAgo)
    {
        string context = Path.Combine(root, "Contexts", id);
        Directory.CreateDirectory(context);
        string recording = Path.Combine(context, "match.mp4");
        File.WriteAllBytes(recording, new byte[RecordingBytes]);
        File.SetLastWriteTimeUtc(recording, Now.AddDays(-recordedDaysAgo).UtcDateTime);
        File.WriteAllText(Path.Combine(context, Entry), "{\"ReplayId\":" + id + "}");
        if (plannedDaysAgo is double planned)
        {
            string plan = Path.Combine(context, DryRunRecordings.PlanFileName);
            File.WriteAllText(plan, "{\"Simulated\":true}");
            File.SetLastWriteTimeUtc(plan, Now.AddDays(-planned).UtcDateTime);
        }

        return context;
    }

    private static TimeSpan MaxAgeFrom(params string[] files)
    {
        var builder = new ConfigurationBuilder();
        foreach (string file in files)
        {
            builder.AddJsonFile(file);
        }

        return builder
                .Build()
                .GetSection("Retention")
                .Get<RetentionSettings>()
                ?.DryRunRecordingMaxAge
            ?? TimeSpan.Zero;
    }

    private static string FindRepoFile(string relative)
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relative);
    }
}
