using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.ServiceHost;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Inspection;

/// <summary>
/// #305: spectate's probe identifies on the OBS websocket with one short read-only session, only
/// while OBS runs and only when <c>ServiceHealth:SpectateObsProbe</c> is on.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsWebsocketProbeTests
{
    [Fact]
    public void ProbeOff_OrObsOff_IsNotUsed()
    {
        var sessions = new FakeSessions();

        Assert.Contains(
            "SpectateObsProbe is off",
            new ObsWebsocketProbe(Settings(), probeOn: false, sessions, () => true).NotUsedReason
        );
        Assert.Contains(
            "OBS:Enabled is false",
            new ObsWebsocketProbe(
                new OBSSettings { Enabled = false },
                probeOn: true,
                sessions,
                () => true
            ).NotUsedReason
        );
        Assert.Null(
            new ObsWebsocketProbe(Settings(), probeOn: true, sessions, () => true).NotUsedReason
        );
    }

    [Fact]
    public async Task AClosedObs_IsSkipped_WithoutASocket()
    {
        var sessions = new FakeSessions();

        ServiceDependencyResult result = await new ObsWebsocketProbe(
            Settings(),
            probeOn: true,
            sessions,
            () => false
        ).CheckAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Skipped, result.State);
        Assert.False(result.Failed);
        Assert.Equal(0, sessions.Opened);
    }

    [Fact]
    public async Task AnIdentifiedSocket_IsOk_AfterOneGetVersion_AndIsClosed()
    {
        var sessions = new FakeSessions();

        ServiceDependencyResult result = await new ObsWebsocketProbe(
            Settings(),
            probeOn: true,
            sessions,
            () => true
        ).CheckAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Ok, result.State);
        Assert.Contains("OBS 32.2.2", result.Cause);
        Assert.Contains("obs-websocket 5.7.4", result.Cause);
        Assert.Equal(new[] { "GetVersion" }, sessions.Requests);
        Assert.Equal(1, sessions.Opened);
        Assert.Equal(1, sessions.Disposed);
        Assert.Equal("ws://127.0.0.1:4455", sessions.Endpoint);
    }

    [Fact]
    public async Task AWrongPassword_IsRejected()
    {
        var sessions = new FakeSessions
        {
            OpenFailure = new ObsUnavailableException(
                ObsUnavailableException.AuthenticationFailed,
                "OBS at ws://127.0.0.1:4455 rejected the websocket password."
            ),
        };

        ServiceDependencyResult result = await new ObsWebsocketProbe(
            Settings(),
            probeOn: true,
            sessions,
            () => true
        ).CheckAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Rejected, result.State);
        Assert.Equal(ObsWebsocketProbe.RejectedCode, result.Code);
        Assert.Contains("WebSocketPassword", result.Remediation);
        Assert.DoesNotContain("obs-password-do-not-print", result.Cause + result.Remediation);
    }

    [Fact]
    public async Task ASocketThatDoesNotIdentify_IsUnreachable()
    {
        var sessions = new FakeSessions
        {
            OpenFailure = new ObsUnavailableException(
                ObsUnavailableException.Unreachable,
                "OBS at ws://127.0.0.1:4455 did not answer within 3 s."
            ),
        };

        ServiceDependencyResult result = await new ObsWebsocketProbe(
            Settings(),
            probeOn: true,
            sessions,
            () => true
        ).CheckAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Unreachable, result.State);
        Assert.Equal(ObsWebsocketProbe.UnreachableCode, result.Code);
        Assert.Contains("did not answer", result.Cause);
    }

    private static OBSSettings Settings() =>
        new()
        {
            Enabled = true,
            WebSocketEndpoint = "ws://127.0.0.1:4455",
            WebSocketPassword = "obs-password-do-not-print",
        };

    private sealed class FakeSessions : IObsReadSessionFactory
    {
        public int Opened { get; private set; }
        public int Disposed { get; private set; }
        public string Endpoint { get; private set; }
        public List<string> Requests { get; } = new();
        public Exception OpenFailure { get; init; }

        public IObsReadSession Open(string endpoint, string password)
        {
            Opened++;
            Endpoint = endpoint;
            if (OpenFailure != null)
            {
                throw OpenFailure;
            }

            return new Session(this);
        }

        private sealed class Session(FakeSessions owner) : IObsReadSession
        {
            public JObject Get(string requestType, JObject requestData = null)
            {
                owner.Requests.Add(requestType);
                return new JObject { ["obsVersion"] = "32.2.2", ["obsWebSocketVersion"] = "5.7.4" };
            }

            public void Dispose() => owner.Disposed++;
        }
    }
}
