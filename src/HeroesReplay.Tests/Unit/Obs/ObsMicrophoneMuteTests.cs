using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Inspection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

/// <summary>
/// #314: the owner's decision was mute, not block. Every microphone input is muted, Desktop
/// Audio, media, and browser sources never are, and a mute that fails is a warning.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsMicrophoneMuteTests
{
    [Fact]
    public void Find_IsTheGlobalMicsAndInputCaptureSources_NeverDesktopAudioOrMedia()
    {
        FakeObs obs = FakeObs.Packaged();
        obs.Mic = "Mic/Aux";
        obs.MicSources["Headset"] = false;

        IReadOnlyList<ObsMicrophone> microphones = ObsMicrophones.Find(
            obs.Open("ws://127.0.0.1:4455", string.Empty)
        );

        Assert.Equal(
            new[]
            {
                new ObsMicrophone("Mic/Aux", "wasapi_input_capture", "mic1"),
                new ObsMicrophone("Headset", "wasapi_input_capture", null),
            },
            microphones
        );
        Assert.Equal(new[] { "GetSpecialInputs", "GetInputList" }, obs.Requests);
    }

    [Fact]
    public void NoMicrophone_SendsNoMute()
    {
        FakeObs obs = FakeObs.Packaged();
        var log = new ListLogger();

        IReadOnlyList<string> muted = new ObsMicrophoneMute(log, obs.OpenMicrophones()).MuteAll(
            ObsMicrophoneMute.AtSessionStart
        );

        Assert.Empty(muted);
        Assert.Empty(obs.MutedInputs);
        Assert.Empty(log.Warnings);
    }

    [Fact]
    public void MuteAll_MutesEachLiveMic_AndLeavesDesktopAudioAndOtherSourcesAlone()
    {
        FakeObs obs = FakeObs.Packaged();
        obs.Mic = "Mic/Aux";
        obs.MicSources["Headset"] = false;
        obs.MicSources["Muted line in"] = true;

        IReadOnlyList<string> muted = new ObsMicrophoneMute(
            new ListLogger(),
            obs.OpenMicrophones()
        ).MuteAll(ObsMicrophoneMute.AtSessionStart);

        Assert.Equal(new[] { "Mic/Aux", "Headset" }, muted);
        Assert.Equal(new[] { "Mic/Aux", "Headset" }, obs.MutedInputs);
        Assert.True(obs.MicMuted);
        Assert.True(obs.MicSources["Headset"]);
        Assert.DoesNotContain(FakeObs.DesktopAudio, obs.MutedInputs);
        Assert.All(
            obs.Sent.Where(sent => sent.Type == "SetInputMute"),
            sent => Assert.True((bool)sent.Data["inputMuted"])
        );
    }

    [Fact]
    public void MuteAll_LogsEachMuteOncePerSession_WithTheInputName()
    {
        FakeObs obs = FakeObs.Packaged();
        obs.Mic = "Mic/Aux";
        var log = new ListLogger();
        var mute = new ObsMicrophoneMute(log, obs.OpenMicrophones());

        mute.MuteAll(ObsMicrophoneMute.AtSessionStart);
        obs.MicMuted = false;
        mute.MuteAll(ObsMicrophoneMute.BeforeStartStream);

        Assert.Equal(2, obs.MutedInputs.Count);
        string warning = Assert.Single(log.Warnings);
        Assert.Contains("Mic/Aux", warning, StringComparison.Ordinal);
        Assert.Contains("mic1", warning, StringComparison.Ordinal);
        Assert.Contains("Disabled", warning, StringComparison.Ordinal);

        mute.NewSession();
        obs.MicMuted = false;
        mute.MuteAll(ObsMicrophoneMute.AtSessionStart);

        Assert.Equal(2, log.Warnings.Count);
    }

    [Fact]
    public void MuteAll_AnAlreadyMutedMic_IsNotSentAgain()
    {
        FakeObs obs = FakeObs.Packaged();
        obs.Mic = "Mic/Aux";
        obs.MicMuted = true;
        var log = new ListLogger();

        Assert.Empty(
            new ObsMicrophoneMute(log, obs.OpenMicrophones()).MuteAll(
                ObsMicrophoneMute.AtSessionStart
            )
        );
        Assert.Empty(obs.MutedInputs);
        Assert.Empty(log.Warnings);
    }

    [Fact]
    public void MuteAll_ARefusedMute_IsOneWarning_AndTheNextMicIsStillMuted()
    {
        FakeObs obs = FakeObs.Packaged();
        obs.Mic = "Mic/Aux";
        obs.MicSources["Headset"] = false;
        obs.MuteRefused.Add("Mic/Aux");
        var log = new ListLogger();
        var mute = new ObsMicrophoneMute(log, obs.OpenMicrophones());

        IReadOnlyList<string> first = mute.MuteAll(ObsMicrophoneMute.AtSessionStart);
        mute.MuteAll(ObsMicrophoneMute.BeforeStartStream);

        Assert.Equal(new[] { "Headset" }, first);
        Assert.True(obs.MicSources["Headset"]);
        Assert.False(obs.MicMuted);
        Assert.Single(
            log.Warnings,
            line => line.StartsWith("Could not mute", StringComparison.Ordinal)
        );
        Assert.Contains(log.Warnings, line => line.Contains("Mic/Aux", StringComparison.Ordinal));
    }

    [Fact]
    public void MuteAll_ObsThatCannotBeRead_IsAWarningAndDoesNotThrow()
    {
        FakeObs obs = FakeObs.Packaged();
        obs.Mic = "Mic/Aux";
        obs.Failures["GetInputList"] = new InvalidOperationException("socket closed");
        var log = new ListLogger();
        var mute = new ObsMicrophoneMute(log, obs.OpenMicrophones());

        Assert.Empty(mute.MuteAll(ObsMicrophoneMute.AtSessionStart));
        Assert.Empty(mute.MuteAll(ObsMicrophoneMute.BeforeStartStream));

        Assert.Empty(obs.MutedInputs);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void TheMicrophoneSession_CanOnlyMute_AndTheReadOnlySessionCannot()
    {
        Assert.Equal(
            new[] { nameof(IObsMicrophoneSession.Mute) },
            typeof(IObsMicrophoneSession).GetMethods().Select(method => method.Name)
        );
        Assert.False(ObsReadOnly.IsAllowed("SetInputMute"));
        Assert.Contains("SetInputMute", ObsValidator.RequiredRequests);
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Warnings { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
