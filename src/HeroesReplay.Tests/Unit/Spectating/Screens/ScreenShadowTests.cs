using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using HeroesReplay.Core.Spectating.Screens;
using HeroesReplay.Core.Telemetry;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Screens;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ScreenShadowTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly ListLogger logger = new();
    private readonly FakeClock clock = new(Start);

    [Theory]
    [InlineData(true, true, ShadowVerdict.Agree)]
    [InlineData(false, false, ShadowVerdict.Agree)]
    [InlineData(true, false, ShadowVerdict.Disagree)]
    [InlineData(false, true, ShadowVerdict.Disagree)]
    [InlineData(true, null, ShadowVerdict.MemoryUnknown)]
    [InlineData(false, null, ShadowVerdict.MemoryUnknown)]
    public void Observe_ReturnsTheVerdict(bool ocr, bool? memory, ShadowVerdict expected)
    {
        ScreenShadow shadow = new(logger, clock);

        ShadowObservation observed = shadow.Observe(ScreenState.Home, ocr, memory, "PLAY");

        Assert.Equal(expected, observed.Verdict);
        Assert.Equal(expected, ScreenShadow.VerdictOf(ocr, memory));
    }

    [Fact]
    public void Observe_SteadyDisagreement_WarnsOnceThenOncePerMinuteWithTheCount()
    {
        ScreenShadow shadow = new(logger, clock);

        shadow.Observe(ScreenState.Home, ocr: false, memory: true, "Email Password Log In");
        Assert.Single(logger.Warnings);
        Assert.Contains("since the last warning: 1", logger.Warnings[0]);
        Assert.Contains("Email Password Log In", logger.Warnings[0]);

        foreach (int seconds in new[] { 10, 20, 30, 59 })
        {
            clock.Now = Start.AddSeconds(seconds);
            shadow.Observe(ScreenState.Home, ocr: false, memory: true, "Email Password Log In");
        }

        Assert.Single(logger.Warnings);

        clock.Now = Start.AddSeconds(60);
        shadow.Observe(ScreenState.Home, ocr: false, memory: true, "Email Password Log In");

        Assert.Equal(2, logger.Warnings.Count);
        Assert.Contains("since the last warning: 5", logger.Warnings[1]);
        Assert.Contains("Home", logger.Warnings[1]);
        Assert.Contains("OCR False", logger.Warnings[1]);
        Assert.Contains("memory True", logger.Warnings[1]);
    }

    [Fact]
    public void Observe_DisagreementThatReturnsWithinAMinute_WaitsForTheNextWarning()
    {
        ScreenShadow shadow = new(logger, clock);

        shadow.Observe(ScreenState.MapLoading, ocr: true, memory: false, "WELCOME TO");
        clock.Now = Start.AddSeconds(5);
        shadow.Observe(ScreenState.MapLoading, ocr: false, memory: false, "");
        clock.Now = Start.AddSeconds(10);
        shadow.Observe(ScreenState.MapLoading, ocr: true, memory: false, "WELCOME TO");

        Assert.Single(logger.Warnings);
    }

    [Fact]
    public void Observe_WarningNamesTheMemoryReasonAndAOneLineExcerpt()
    {
        ScreenShadow shadow = new(logger, clock);
        string text = "WELCOME TO\nBRAXIS HOLDOUT " + new string('x', 300);

        shadow.Observe(
            ScreenState.MapLoading,
            ocr: true,
            memory: false,
            text,
            "Match, match, menu seen True"
        );

        string warning = Assert.Single(logger.Warnings);
        Assert.Contains("Match, match, menu seen True", warning);
        Assert.Contains("WELCOME TO BRAXIS HOLDOUT", warning);
        Assert.DoesNotContain("\n", warning);
        Assert.Contains(new string('x', 100) + "...", warning);
        Assert.DoesNotContain(new string('x', 200), warning);
    }

    [Fact]
    public void Observe_FrameOncePerStatePerFiveMinutes()
    {
        ScreenShadow shadow = new(logger, clock);

        Assert.True(shadow.Observe(ScreenState.Home, false, true, "").SaveFrame);
        clock.Now = Start.AddMinutes(1);
        Assert.False(shadow.Observe(ScreenState.Home, false, true, "").SaveFrame);
        clock.Now = Start.AddMinutes(4.9);
        Assert.False(shadow.Observe(ScreenState.Home, false, true, "").SaveFrame);
        clock.Now = Start.AddMinutes(5);
        Assert.True(shadow.Observe(ScreenState.Home, false, true, "").SaveFrame);
    }

    [Fact]
    public void Observe_FrameLimitIsPerState()
    {
        ScreenShadow shadow = new(logger, clock);

        Assert.True(shadow.Observe(ScreenState.Home, false, true, "").SaveFrame);
        Assert.True(shadow.Observe(ScreenState.MapLoading, true, false, "WELCOME TO").SaveFrame);
        Assert.False(shadow.Observe(ScreenState.Home, false, true, "").SaveFrame);
        Assert.False(shadow.Observe(ScreenState.MapLoading, true, false, "WELCOME TO").SaveFrame);
    }

    [Fact]
    public void Observe_FrameIntervalCanBeOverridden()
    {
        ScreenShadow shadow = new(logger, clock, TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), shadow.FrameInterval);
        Assert.True(shadow.Observe(ScreenState.Home, false, true, "").SaveFrame);
        clock.Now = Start.AddSeconds(30);
        Assert.True(shadow.Observe(ScreenState.Home, false, true, "").SaveFrame);
        Assert.Equal(ScreenShadow.DefaultFrameInterval, new ScreenShadow(logger).FrameInterval);
    }

    [Fact]
    public void Observe_AgreementNeverWarnsOrSavesAFrame()
    {
        ScreenShadow shadow = new(logger, clock);

        Assert.False(shadow.Observe(ScreenState.Home, true, true, "PLAY").SaveFrame);
        Assert.False(shadow.Observe(ScreenState.MapLoading, false, false, "").SaveFrame);

        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void Observe_MemoryUnknown_NeverWarnsOrSavesAFrame()
    {
        ScreenShadow shadow = new(logger, clock);

        for (int minute = 0; minute <= 10; minute++)
        {
            clock.Now = Start.AddMinutes(minute);
            ShadowObservation observed = shadow.Observe(
                ScreenState.VersionMismatch,
                ocr: minute % 2 == 0,
                memory: null,
                "Version mismatch"
            );
            Assert.Equal(ShadowVerdict.MemoryUnknown, observed.Verdict);
            Assert.False(observed.SaveFrame);
        }

        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void Observe_LogsInformationOnlyWhenThePairChanges()
    {
        ScreenShadow shadow = new(logger, clock);

        shadow.Observe(ScreenState.Home, ocr: true, memory: true, "PLAY");
        shadow.Observe(ScreenState.Home, ocr: true, memory: true, "PLAY");
        shadow.Observe(ScreenState.Home, ocr: true, memory: true, "PLAY");
        shadow.Observe(ScreenState.Home, ocr: true, memory: null, "PLAY");
        shadow.Observe(ScreenState.Home, ocr: true, memory: null, "PLAY");
        shadow.Observe(ScreenState.MapLoading, ocr: false, memory: false, "PLAY");

        Assert.Equal(
            new[] { LogLevel.Information, LogLevel.Debug, LogLevel.Debug },
            logger.Entries.Take(3).Select(entry => entry.Level)
        );
        Assert.Equal(3, logger.Entries.Count(entry => entry.Level == LogLevel.Information));
        Assert.Equal(3, logger.Entries.Count(entry => entry.Level == LogLevel.Debug));
        Assert.Contains(
            logger.Entries,
            entry =>
                entry.Level == LogLevel.Information
                && entry.Message.Contains("Home")
                && entry.Message.Contains("memory unknown (not read)")
        );
    }

    [Fact]
    public void Observe_CountsEveryObservationByStateAndVerdict()
    {
        var measured = new List<(string State, string Verdict)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (
                instrument.Meter.Name == HeroesReplayTelemetry.SourceName
                && instrument.Name == HeroesReplayTelemetry.ScreenShadowInstrument
            )
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (_, value, tags, _) =>
            {
                string state = null;
                string verdict = null;
                foreach (KeyValuePair<string, object> tag in tags)
                {
                    if (tag.Key == "state")
                    {
                        state = tag.Value as string;
                    }
                    else if (tag.Key == "verdict")
                    {
                        verdict = tag.Value as string;
                    }
                }

                Assert.Equal(1, value);
                lock (measured)
                {
                    measured.Add((state, verdict));
                }
            }
        );
        listener.Start();

        ScreenShadow shadow = new(logger, clock);
        shadow.Observe(ScreenState.MapLoading, true, true, "WELCOME TO");
        shadow.Observe(ScreenState.MapLoading, true, false, "WELCOME TO");
        shadow.Observe(ScreenState.GameDataDownload, false, null, "");

        Assert.Contains(("map_loading", "agree"), measured);
        Assert.Contains(("map_loading", "disagree"), measured);
        Assert.Contains(("game_data_download", "memory_unknown"), measured);
    }

    [Fact]
    public void Tags_AreSnakeCaseForEveryStateAndVerdict()
    {
        Assert.Equal("home", ScreenShadow.TagOf(ScreenState.Home));
        Assert.Equal("login_form", ScreenShadow.TagOf(ScreenState.LoginForm));
        Assert.Equal("map_loading", ScreenShadow.TagOf(ScreenState.MapLoading));
        Assert.Equal("game_data_download", ScreenShadow.TagOf(ScreenState.GameDataDownload));
        Assert.Equal("game_data_startup", ScreenShadow.TagOf(ScreenState.GameDataStartup));
        Assert.Equal("version_mismatch", ScreenShadow.TagOf(ScreenState.VersionMismatch));
        Assert.Equal("region_unavailable", ScreenShadow.TagOf(ScreenState.RegionUnavailable));
        Assert.Equal("end_screen", ScreenShadow.TagOf(ScreenState.EndScreen));
        Assert.Equal("agree", ScreenShadow.TagOf(ShadowVerdict.Agree));
        Assert.Equal("disagree", ScreenShadow.TagOf(ShadowVerdict.Disagree));
        Assert.Equal("memory_unknown", ScreenShadow.TagOf(ShadowVerdict.MemoryUnknown));
    }

    [Fact]
    public void FrameFileName_IsTheStateAndTheTime()
    {
        Assert.Equal(
            "map_loading-20261008-071502.png",
            ScreenShadow.FrameFileName(
                ScreenState.MapLoading,
                new DateTimeOffset(2026, 10, 8, 7, 15, 2, TimeSpan.FromHours(1))
            )
        );
    }

    [Fact]
    public void Excerpt_IsOneLineOfAtMost160Characters()
    {
        Assert.Equal("(empty)", ScreenShadow.Excerpt("  "));
        Assert.Equal("(empty)", ScreenShadow.Excerpt(null));
        Assert.Equal("PLAY COLLECTION", ScreenShadow.Excerpt("PLAY\nCOLLECTION "));
        string excerpt = ScreenShadow.Excerpt(new string('a', 400));
        Assert.Equal(new string('a', ScreenShadow.ExcerptLength) + "...", excerpt);
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public List<string> Warnings =>
            Entries
                .Where(entry => entry.Level == LogLevel.Warning)
                .Select(entry => entry.Message)
                .ToList();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class FakeClock : TimeProvider
    {
        public FakeClock(DateTimeOffset now)
        {
            Now = now;
        }

        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
