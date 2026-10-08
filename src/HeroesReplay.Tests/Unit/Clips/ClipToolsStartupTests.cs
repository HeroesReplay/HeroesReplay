using System;
using System.Collections.Generic;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.Clips;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClipToolsStartupTests
{
    private const string Installed = @"C:\heroesreplay\tools\ffmpeg";

    [Fact]
    public void ClipsOn_AMissingTool_IsOneError()
    {
        var logger = new ListLogger();
        var locator = Locator(Installed + @"\ffmpeg.exe");

        bool ok = ClipToolsStartup.Check(Settings(recording: true), logger, locator);

        Assert.False(ok);
        (LogLevel level, string message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains("ffprobe.exe", message);
        Assert.DoesNotContain("ffmpeg.exe and", message);
        Assert.Contains("heroesreplay deps install", message);
        Assert.Contains(Installed, message);
    }

    [Fact]
    public void ClipsOn_BothMissing_IsStillOneError()
    {
        var logger = new ListLogger();

        ClipToolsStartup.Check(Settings(recording: true), logger, Locator());

        (_, string message) = Assert.Single(logger.Entries);
        Assert.Contains("ffmpeg.exe and ffprobe.exe", message);
    }

    [Fact]
    public void ClipsOn_BothFound_LogsNothing()
    {
        var logger = new ListLogger();
        var locator = Locator(Installed + @"\ffmpeg.exe", @"C:\ffmpeg\bin\ffprobe.exe");

        Assert.True(ClipToolsStartup.Check(Settings(recording: true), logger, locator));
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void ClipsOff_LogsNothing()
    {
        var logger = new ListLogger();

        Assert.True(ClipToolsStartup.Check(Settings(recording: false), logger, Locator()));
        Assert.Empty(logger.Entries);
        Assert.Empty(ClipToolsStartup.Missing(new AppSettings(), Locator()));
    }

    private static AppSettings Settings(bool recording) =>
        new() { OBS = new OBSSettings { RecordingEnabled = recording } };

    private static FfmpegLocator Locator(params string[] files)
    {
        var present = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        return new FfmpegLocator(null, Installed, string.Empty, present.Contains);
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
