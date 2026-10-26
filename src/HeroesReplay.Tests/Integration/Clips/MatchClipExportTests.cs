using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Integration.Clips;

/// <summary>
/// Runs the real ffprobe and ffmpeg against a 1920x1080 mp4 written the way OBS writes one, with
/// a real replay that has a pentakill: a plain MP4 (index at the end of the file), a fragmented
/// MP4 (OBS's fragmented_mp4 muxer flags, #310), and a fragmented MP4 cut off mid-write the way a
/// killed obs-ffmpeg-mux leaves it.
/// Needs ffmpeg where clips look for it (FfmpegLocator): the deps install folder, C:\ffmpeg\bin, or PATH.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Integration)]
public class MatchClipExportTests : IClassFixture<ReplayFixture>
{
    private const int LeadSeconds = 30;

    /// <summary>What OBS 32.2.2 gives obs-ffmpeg-mux for a fragmented_* recording format.</summary>
    private const string FragmentedMovFlags = "frag_keyframe+empty_moov+delay_moov";

    private readonly ReplayFixture fixture;

    public MatchClipExportTests(ReplayFixture fixture)
    {
        this.fixture = fixture;
    }

    public enum RecordingLayout
    {
        Plain,
        Fragmented,

        /// <summary>Fragmented, with the last 15% of the file missing: OBS and its muxer were killed.</summary>
        FragmentedCutOff,
    }

    [Theory]
    [InlineData(RecordingLayout.Plain)]
    [InlineData(RecordingLayout.Fragmented)]
    [InlineData(RecordingLayout.FragmentedCutOff)]
    public async Task ExportAsync_ReadsTheDurationAndCutsAFullFrameClip(RecordingLayout layout)
    {
        // Real pentakills are rare, so the window is built from the first five real killing blows.
        TeamKillBlow[] blows = TeamKillDeaths
            .FromReplay(fixture.Replay, null)
            .OrderBy(death => death.Second)
            .Take(5)
            .Select(death => new TeamKillBlow(death.Second, death.VictimHero))
            .ToArray();
        var first = new TeamKillClip(
            TeamKillClips.PentakillKind,
            "Test",
            blows[0].Second,
            blows[0].Second + 10,
            blows[0].Second - 12,
            blows[0].Second + 18,
            "Test pentakill",
            blows
        );
        // A triple kill (#511) inside the same recording, cut as its own clip with its own entry.
        var triple = new TeamKillClip(
            TeamKillClips.TripleKillKind,
            "Test",
            blows[0].Second + 2,
            blows[0].Second + 4,
            blows[0].Second - 10,
            blows[0].Second + 12,
            "Test triple kill",
            blows.Take(3).ToArray()
        );
        // A boss stolen from the team that killed it (#528) names the capturing team, not a hero.
        var steal = new TeamKillClip(
            BossFightClips.StealKind,
            "Red team",
            blows[0].Second + 1,
            blows[0].Second + 6,
            blows[0].Second - 11,
            blows[0].Second + 12,
            "Red steals the boss Blue killed",
            NamesTeam: true
        );
        int length = first.HudEndSecond - first.HudStartSecond + LeadSeconds * 2;
        string directory = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-clips-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            string recording = Path.Combine(directory, "2026-10-02 12-00-00.mp4");
            Run(
                "ffmpeg",
                "-v error -f lavfi -i testsrc2=size=1920x1080:rate=10 -t "
                    + length.ToString(CultureInfo.InvariantCulture)
                    + " -c:v libx264 -preset ultrafast -g 20 -pix_fmt yuv420p "
                    + (
                        layout == RecordingLayout.Plain
                            ? string.Empty
                            : "-movflags " + FragmentedMovFlags + " "
                    )
                    + "\""
                    + recording
                    + "\""
            );
            if (layout == RecordingLayout.FragmentedCutOff)
            {
                using var file = new FileStream(recording, FileMode.Open, FileAccess.ReadWrite);
                file.SetLength(file.Length * 85 / 100);
            }

            // The recording starts LeadSeconds before the clip on the match clock.
            var clock = new RecordingClock();
            clock.Start();
            int hudStart = first.HudStartSecond - LeadSeconds;
            for (int second = 0; second <= length; second++)
            {
                clock.ObserveAt(TimeSpan.FromSeconds(hudStart + second), second);
            }

            var logger = new ListLogger();
            await MatchClipExporter.ExportAsync(
                new[] { first, triple, steal },
                fixture.Replay,
                65000001,
                directory,
                recording,
                clock,
                new YouTubeSettings { PrivacyStatus = "private", TitlePrefix = "[TEST]" },
                "youtube-entry.json",
                logger
            );

            string read = Assert.Single(
                logger.Lines,
                line => line.StartsWith("Read duration ", StringComparison.Ordinal)
            );
            double probed = double.Parse(
                read.Substring("Read duration ".Length).Split('s')[0],
                CultureInfo.InvariantCulture
            );
            if (layout == RecordingLayout.FragmentedCutOff)
            {
                // The fragments before the cut are read; the cut-off one is not.
                Assert.InRange(probed, length * 0.6, length * 0.9);
            }
            else
            {
                Assert.InRange(probed, length - 1, length + 1);
            }

            string index = Path.Combine(directory, "clips.json");
            Assert.True(File.Exists(index), string.Join(Environment.NewLine, logger.Lines));
            using JsonDocument rows = JsonDocument.Parse(File.ReadAllText(index));
            JsonElement row = rows
                .RootElement.EnumerateArray()
                .First(element =>
                    element.GetProperty("hudStart").GetInt32() == first.HudStartSecond
                );
            string clip = row.GetProperty("file").GetString();
            Assert.True(File.Exists(clip));
            Assert.True(
                File.Exists(Path.Combine(Path.GetDirectoryName(clip), "youtube-entry.json"))
            );

            string[] probe = Run(
                    "ffprobe",
                    "-v error -select_streams v:0 -show_entries stream=width,height:format=duration -of csv=p=0 \""
                        + clip
                        + "\""
                )
                .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("1920", probe[0]);
            Assert.Equal("1080", probe[1]);
            double expected = first.HudEndSecond - first.HudStartSecond;
            double actual = double.Parse(probe[2], CultureInfo.InvariantCulture);
            Assert.InRange(actual, expected - 1, expected + 1);

            // Each kind names itself in its title and tags, with the English map name.
            YouTubeEntry pentakillEntry = EntryOf(rows, first.HudStartSecond);
            YouTubeEntry tripleEntry = EntryOf(rows, triple.HudStartSecond);
            Assert.Contains(" - pentakill - Garden of Terror - 65000001", pentakillEntry.Title);
            Assert.Equal(
                "[TEST] Test - triple kill - Garden of Terror - 65000001",
                tripleEntry.Title
            );
            Assert.Contains("triple kill", tripleEntry.Tags);
            Assert.Equal("clip:65000001:triple-kill:Test", tripleEntry.DescriptionLines[0]);
            YouTubeEntry stealEntry = EntryOf(rows, steal.HudStartSecond);
            Assert.Equal(
                "[TEST] Red team - boss steal - Garden of Terror - 65000001",
                stealEntry.Title
            );
            Assert.Null(stealEntry.Hero);
            Assert.Null(stealEntry.Heroes);
            Assert.Equal(new[] { "boss steal", "Garden of Terror" }, stealEntry.Tags);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException) { }
        }
    }

    private static YouTubeEntry EntryOf(JsonDocument rows, int hudStart)
    {
        string clip = rows
            .RootElement.EnumerateArray()
            .First(element => element.GetProperty("hudStart").GetInt32() == hudStart)
            .GetProperty("file")
            .GetString();
        return JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(clip), "youtube-entry.json")),
            YouTubeJsonContext.Default.YouTubeEntry
        );
    }

    private static string Run(string tool, string arguments)
    {
        string path = FfmpegLocator.From(null, null).Find(tool);
        Assert.False(string.IsNullOrWhiteSpace(path), tool + " was not found.");
        using Process process = Process.Start(
            new ProcessStartInfo(path, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        );
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, tool + " failed: " + error);
        return output;
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Lines { get; } = new();

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            Lines.Add(formatter(state, exception));
        }
    }
}
