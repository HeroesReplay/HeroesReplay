using System;
using System.Collections.Generic;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Inspection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsFailSafeTests
{
    [Fact]
    public void LiveStream_ShowsTheWaitingScene()
    {
        var session = new FakeSession { Live = true };

        string result = Apply(ObsFailSafeAction.WaitingScene, session);

        Assert.Equal(new[] { "scene:waiting-screen" }, session.Changes);
        Assert.Contains("waiting scene 'waiting-screen'", result);
        Assert.True(session.Disposed);
    }

    [Fact]
    public void LiveStream_StopStream_StopsIt()
    {
        var session = new FakeSession { Live = true };

        string result = Apply(ObsFailSafeAction.StopStream, session);

        Assert.Equal(new[] { "stop" }, session.Changes);
        Assert.Contains("Stopped the OBS stream", result);
    }

    [Theory]
    [InlineData(ObsFailSafeAction.StopStream, "stop")]
    [InlineData(ObsFailSafeAction.WaitingScene, "scene:waiting-screen")]
    public void AStreamStuckReconnecting_IsStillMadeSafe(ObsFailSafeAction action, string change)
    {
        // #395: an active output that is reconnecting can go back on air, so it counts.
        var session = new FakeSession { Live = true, Reconnecting = true };

        string result = Apply(action, session);

        Assert.Equal(new[] { change }, session.Changes);
        Assert.Contains("reconnecting", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AStreamThatIsNotLive_IsLeftAlone()
    {
        var session = new FakeSession { Live = false };

        Assert.Contains(
            "not live",
            Apply(ObsFailSafeAction.WaitingScene, session),
            StringComparison.Ordinal
        );
        Assert.Empty(session.Changes);
    }

    [Theory]
    [InlineData(ObsFailSafeAction.None, true, true, true)]
    [InlineData(ObsFailSafeAction.WaitingScene, false, true, true)]
    [InlineData(ObsFailSafeAction.WaitingScene, true, false, true)]
    [InlineData(ObsFailSafeAction.WaitingScene, true, true, false)]
    public void WhenThisInstallDidNotStreamHere_OBSIsNotOpened(
        ObsFailSafeAction action,
        bool streamingEnabled,
        bool armed,
        bool obsRunning
    )
    {
        int opened = 0;

        ObsFailSafe.Apply(
            action,
            Settings(streamingEnabled),
            armed,
            obsRunning,
            () =>
            {
                opened++;
                return new FakeSession { Live = true };
            }
        );

        Assert.Equal(0, opened);
    }

    [Fact]
    public void UnreachableOrRefusingObs_IsReportedNotThrown()
    {
        string unreachable = ObsFailSafe.Apply(
            ObsFailSafeAction.WaitingScene,
            Settings(true),
            armed: true,
            obsRunning: true,
            () =>
                throw new ObsUnavailableException(
                    ObsUnavailableException.Unreachable,
                    "OBS did not answer."
                )
        );
        string refused = Apply(
            ObsFailSafeAction.WaitingScene,
            new FakeSession
            {
                Live = true,
                SceneError = new ObsRequestException("SetCurrentProgramScene", 600, "No scene."),
            }
        );

        Assert.Contains("OBS was not reached", unreachable);
        Assert.Contains("OBS refused the change", refused);
    }

    private static string Apply(ObsFailSafeAction action, FakeSession session) =>
        ObsFailSafe.Apply(action, Settings(true), armed: true, obsRunning: true, () => session);

    private static OBSSettings Settings(bool streaming) =>
        new() { StreamingEnabled = streaming, WaitingSceneName = "waiting-screen" };

    private sealed class FakeSession : IObsFailSafeSession
    {
        public bool Live { get; init; }
        public bool Reconnecting { get; init; }
        public Exception SceneError { get; init; }
        public List<string> Changes { get; } = new();
        public bool Disposed { get; private set; }

        public JObject Get(string requestType, JObject requestData = null)
        {
            Assert.True(ObsReadOnly.IsAllowed(requestType), requestType);
            return requestType == "GetStreamStatus"
                ? new JObject { ["outputActive"] = Live, ["outputReconnecting"] = Reconnecting }
                : new JObject();
        }

        public void ShowScene(string sceneName)
        {
            if (SceneError != null)
            {
                throw SceneError;
            }

            Changes.Add("scene:" + sceneName);
        }

        public void StopStream() => Changes.Add("stop");

        public void Dispose() => Disposed = true;
    }
}
