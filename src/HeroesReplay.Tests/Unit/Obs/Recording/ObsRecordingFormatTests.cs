using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Obs.Recording;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Recording;

/// <summary>
/// #310: the machine owns its OBS profile, so the spectator sets the recording format right
/// before each StartRecord, in the section the active output mode reads, and reads it back. A
/// set that fails or does not stick is logged, and the recording starts anyway.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsRecordingFormatTests
{
    private const int ReplayId = 65822779;

    private readonly ListLogger logger = new();

    [Theory]
    [InlineData("Simple", "SimpleOutput", "AdvOut")]
    [InlineData("Advanced", "AdvOut", "SimpleOutput")]
    public void BeforeStartRecord_TheActiveModesFormatIsSetAndReadBack(
        string mode,
        string category,
        string otherCategory
    )
    {
        FakeObs obs = Idle(mode, current: "mp4");
        obs.ProfileParameters[(otherCategory, "RecFormat2")] = "mkv";

        (ObsRecordingResult started, ObsRecordingFormatResult format) = StartRecording(
            obs,
            ObsRecordingFormat.FragmentedMp4
        );

        Assert.True(started.Succeeded);
        Assert.Equal(ObsRecordingFormatState.Set, format.State);
        Assert.Equal(category, format.Category);
        Assert.Equal("mp4", format.Before);
        Assert.Equal("fragmented_mp4", format.ReadBack);
        Assert.Equal(
            new[]
            {
                "GetProfileParameter",
                "GetProfileParameter",
                "SetProfileParameter",
                "GetProfileParameter",
                "StartRecord",
            },
            obs.Requests
        );
        JObject set = Assert.Single(obs.Sent, sent => sent.Type == "SetProfileParameter").Data;
        Assert.Equal(category, (string)set["parameterCategory"]);
        Assert.Equal("RecFormat2", (string)set["parameterName"]);
        Assert.Equal("fragmented_mp4", (string)set["parameterValue"]);
        Assert.Equal("fragmented_mp4", obs.ProfileParameters[(category, "RecFormat2")]);
        // The section the other output mode reads is left alone.
        Assert.Equal("mkv", obs.ProfileParameters[(otherCategory, "RecFormat2")]);
        (LogLevel level, string message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains($"replay {ReplayId} is fragmented_mp4", message, StringComparison.Ordinal);
        Assert.Contains("was mp4", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AProfileThatAlreadyRecordsTheFormat_IsNotWritten()
    {
        FakeObs obs = Idle("Simple", current: "fragmented_mp4");

        (ObsRecordingResult started, ObsRecordingFormatResult format) = StartRecording(obs, null);

        Assert.True(started.Succeeded);
        Assert.Equal(ObsRecordingFormatState.Unchanged, format.State);
        Assert.DoesNotContain("SetProfileParameter", obs.Requests);
        Assert.Equal("StartRecord", obs.Requests.Last());
    }

    [Fact]
    public void AReadBackThatDiffers_IsAWarning_AndTheRecordingStarts()
    {
        FakeObs obs = Idle("Simple", current: "mp4");
        obs.IgnoreProfileWrites = true;

        (ObsRecordingResult started, ObsRecordingFormatResult format) = StartRecording(
            obs,
            ObsRecordingFormat.FragmentedMp4
        );

        Assert.Equal(ObsRecordingFormatState.Mismatch, format.State);
        Assert.Equal("mp4", format.ReadBack);
        Assert.True(started.Succeeded);
        Assert.Equal("StartRecord", obs.Requests.Last());
        (LogLevel level, string message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains(
            "SimpleOutput/RecFormat2 reads mp4 after it was set to fragmented_mp4",
            message,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("SetProfileParameter")]
    [InlineData("GetProfileParameter")]
    public void AFailedRequest_IsAWarning_AndTheRecordingStarts(string failing)
    {
        FakeObs obs = Idle("Simple", current: "mp4");
        obs.Failures[failing] = new ObsRequestException(failing, 604, "No profile is loaded.");

        (ObsRecordingResult started, ObsRecordingFormatResult format) = StartRecording(
            obs,
            ObsRecordingFormat.FragmentedMp4
        );

        Assert.Equal(ObsRecordingFormatState.Failed, format.State);
        Assert.True(started.Succeeded);
        Assert.Equal("StartRecord", obs.Requests.Last());
        (LogLevel level, string message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains(
            "Could not set the OBS recording format",
            message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void AnInvalidSetting_SendsNoProfileRequest_AndTheRecordingStarts()
    {
        FakeObs obs = Idle("Simple", current: "mp4");

        (ObsRecordingResult started, ObsRecordingFormatResult format) = StartRecording(obs, "mov");

        Assert.Equal(ObsRecordingFormatState.Invalid, format.State);
        Assert.True(started.Succeeded);
        Assert.Equal(new[] { "StartRecord" }, obs.Requests);
        Assert.Equal("mp4", obs.ProfileParameters[("SimpleOutput", "RecFormat2")]);
        (LogLevel level, string message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains("'mov'", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AChangeToHybridMp4_SaysItNeedsAnObsRestart()
    {
        FakeObs obs = Idle("Simple", current: "fragmented_mp4");

        StartRecording(obs, ObsRecordingFormat.HybridMp4);

        (LogLevel level, string message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains("after OBS restarts", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "fragmented_mp4")]
    [InlineData("", "fragmented_mp4")]
    [InlineData("  ", "fragmented_mp4")]
    [InlineData("mp4", "mp4")]
    [InlineData("HYBRID_MP4", "hybrid_mp4")]
    [InlineData(" fragmented_mp4 ", "fragmented_mp4")]
    [InlineData("mkv", "mkv")]
    [InlineData("mov", null)]
    [InlineData("fragmented_mov", null)]
    [InlineData(".mp4", null)]
    public void TheSetting_AcceptsOnlyTheFourFormats(string configured, string resolved)
    {
        Assert.Equal(resolved, ObsRecordingFormat.Resolve(configured));
        string error = ObsRecordingFormat.Validate(configured);
        if (resolved == null)
        {
            Assert.Contains(
                "mp4, hybrid_mp4, fragmented_mp4, mkv",
                error,
                StringComparison.Ordinal
            );
        }
        else
        {
            Assert.Null(error);
        }
    }

    [Theory]
    [InlineData("fragmented_mp4", true)]
    [InlineData("FRAGMENTED_MP4", true)]
    [InlineData("mp4", false)]
    [InlineData("hybrid_mp4", false)]
    [InlineData("mkv", false)]
    [InlineData(null, false)]
    public void OnlyFragmentedMp4_IsCrashSafe(string format, bool crashSafe)
    {
        Assert.Equal(crashSafe, ObsRecordingFormat.IsCrashSafe(format));
    }

    [Theory]
    [InlineData("appsettings.dev.json")]
    [InlineData("appsettings.prod.json")]
    public void EveryEnvironment_RecordsFragmentedMp4(string overlay)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, overlay))
            .Build();

        OBSSettings obs = configuration.GetSection("OBS").Get<OBSSettings>();

        Assert.Equal(ObsRecordingFormat.FragmentedMp4, obs.RecordingFormat);
        Assert.Equal(ObsRecordingFormat.FragmentedMp4, new OBSSettings().RecordingFormat);
    }

    [Fact]
    public void TheSessionCanWriteOnlyTheRecordingFormat()
    {
        Assert.Equal("SimpleOutput", ObsRecordingFormat.Category("Simple"));
        Assert.Equal("SimpleOutput", ObsRecordingFormat.Category(null));
        Assert.Equal("AdvOut", ObsRecordingFormat.Category("advanced"));
        Assert.Equal(
            new[] { "SetRecordingFormat" },
            typeof(IObsRecordFormatSession).GetMethods().Select(method => method.Name)
        );
    }

    /// <summary>An OBS in <paramref name="mode"/> output that records <paramref name="current"/> and is not recording.</summary>
    private static FakeObs Idle(string mode, string current)
    {
        FakeObs obs = FakeObs.Packaged();
        obs.Recording = false;
        obs.ProfileParameters[("Output", "Mode")] = mode;
        obs.ProfileParameters[
            (ObsRecordingFormat.Category(mode), ObsRecordingFormat.ParameterName)
        ] = current;
        return obs;
    }

    /// <summary>The recording start the spectator makes, with the format step as its output preparation.</summary>
    private (ObsRecordingResult Started, ObsRecordingFormatResult Format) StartRecording(
        FakeObs obs,
        string configured
    )
    {
        var session = new RecordingSession(
            NullLogger.Instance,
            obs.RecordSocket(),
            new ObsRecordingBudget
            {
                RetryCount = 0,
                RetryDelay = TimeSpan.Zero,
                StartTimeout = TimeSpan.FromMilliseconds(40),
                StopTimeout = TimeSpan.FromMilliseconds(40),
                PollInterval = TimeSpan.FromMilliseconds(5),
            }
        );
        ObsRecordingFormatResult format = null;
        ObsRecordingResult started = session.StartRecording(
            () => true,
            null,
            ReplayId,
            "unit",
            () =>
                format = ObsRecordingFormat.Apply(
                    obs.OpenRecordFormat(),
                    configured,
                    logger,
                    ReplayId
                )
        );
        return (started, format);
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (IsEnabled(logLevel))
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
