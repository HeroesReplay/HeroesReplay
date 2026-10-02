using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using HeroesReplay.Core.Obs;
using OBSWebsocketDotNet;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsReadSessionTests
{
    [Fact]
    public void AllowList_HasOnlyGetRequests()
    {
        Assert.NotEmpty(ObsReadOnly.Requests);
        Assert.All(
            ObsReadOnly.Requests,
            request => Assert.StartsWith("Get", request, StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData("StartStream")]
    [InlineData("StopStream")]
    [InlineData("ToggleStream")]
    [InlineData("StartRecord")]
    [InlineData("StopRecord")]
    [InlineData("SetCurrentProgramScene")]
    [InlineData("SetCurrentProfile")]
    [InlineData("SetCurrentSceneCollection")]
    [InlineData("SetStreamServiceSettings")]
    [InlineData("SetInputSettings")]
    [InlineData("SetInputMute")]
    [InlineData("SetRecordDirectory")]
    [InlineData("CreateInput")]
    [InlineData("RemoveInput")]
    [InlineData("RemoveScene")]
    [InlineData("SaveSourceScreenshot")]
    [InlineData("GetSomethingNew")]
    [InlineData("getVersion")]
    [InlineData("")]
    [InlineData(null)]
    public void WebsocketSession_RefusesAnythingElse_BeforeSending(string requestType)
    {
        // Never connected: the refusal can only come from the guard, not from OBS.
        using var session = new ObsWebsocketReadSession(new OBSWebsocket());

        var error = Assert.Throws<InvalidOperationException>(() => session.Get(requestType));
        Assert.Contains("not a read-only OBS request", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_ClosedPort_FailsFastWithAnActionableCode()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var clock = Stopwatch.StartNew();

        var error = Assert.Throws<ObsUnavailableException>(() =>
            new ObsWebsocketReadSessionFactory().Open("ws://127.0.0.1:" + port, string.Empty)
        );

        clock.Stop();
        Assert.Equal(ObsUnavailableException.Unreachable, error.Code);
        Assert.Contains("ws://127.0.0.1:" + port, error.Message, StringComparison.Ordinal);
        Assert.Contains("WebSocket Server Settings", error.Message, StringComparison.Ordinal);
        Assert.True(
            clock.Elapsed
                < ObsWebsocketReadSessionFactory.IdentifyTimeout + TimeSpan.FromSeconds(2),
            clock.Elapsed.ToString()
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://127.0.0.1:4455")]
    public void Open_BadEndpoint_IsUnreachable(string endpoint)
    {
        var error = Assert.Throws<ObsUnavailableException>(() =>
            new ObsWebsocketReadSessionFactory().Open(endpoint, null)
        );

        Assert.Equal(ObsUnavailableException.Unreachable, error.Code);
        Assert.Contains("OBS:WebSocketEndpoint", error.Message, StringComparison.Ordinal);
    }
}
