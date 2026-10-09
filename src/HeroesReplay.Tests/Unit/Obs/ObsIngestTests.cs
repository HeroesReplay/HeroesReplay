using HeroesReplay.Core.Obs;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

/// <summary>#407: where the ingest check before StartStream connects.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsIngestTests
{
    [Fact]
    public void TwitchAuto_IsTheGlobalIngest()
    {
        ObsIngestTarget target = ObsIngest.Resolve(
            new ObsStreamServer("rtmp_common", "Twitch", "auto")
        );

        Assert.Equal(new ObsIngestTarget("ingest.global-contribute.live-video.net", 1935), target);
        Assert.Equal("ingest.global-contribute.live-video.net:1935", target.ToString());
    }

    [Theory]
    [InlineData("rtmp://127.0.0.1:19350/app", "127.0.0.1", 19350)]
    [InlineData(
        "rtmp://lhr03.contribute.live-video.net/app",
        "lhr03.contribute.live-video.net",
        1935
    )]
    [InlineData("rtmps://a.rtmps.youtube.com:443/live2", "a.rtmps.youtube.com", 443)]
    [InlineData("rtmps://live-api-s.facebook.com/rtmp/", "live-api-s.facebook.com", 443)]
    [InlineData("127.0.0.1:19350", "127.0.0.1", 19350)]
    [InlineData(" RTMP://Relay.Local/app ", "relay.local", 1935)]
    public void AServerUrl_IsItsHostAndPort(string server, string host, int port)
    {
        Assert.Equal(
            new ObsIngestTarget(host, port),
            ObsIngest.Resolve(new ObsStreamServer("rtmp_custom", null, server))
        );
    }

    [Theory]
    [InlineData("rtmp_common", "YouTube - RTMPS", "auto")]
    [InlineData("rtmp_custom", null, "auto")]
    [InlineData("rtmp_custom", null, "srt://127.0.0.1:9000")]
    [InlineData("whip_custom", null, "https://whip.example/endpoint")]
    [InlineData("rtmp_custom", null, "")]
    [InlineData("rtmp_custom", null, null)]
    public void AServerItCannotCheck_IsNotChecked(string type, string service, string server)
    {
        Assert.Null(ObsIngest.Resolve(new ObsStreamServer(type, service, server)));
    }

    [Fact]
    public void StreamServer_ReadsTypeServiceAndServerOnly()
    {
        var response = new JObject
        {
            ["streamServiceType"] = "rtmp_common",
            ["streamServiceSettings"] = new JObject
            {
                ["service"] = "Twitch",
                ["server"] = "auto",
                ["key"] = FakeObs.StreamKey,
                ["password"] = "secret",
            },
        };

        ObsStreamServer server = ObsStreamServer.From(response);

        Assert.Equal(new ObsStreamServer("rtmp_common", "Twitch", "auto"), server);
        Assert.DoesNotContain(
            FakeObs.StreamKey,
            server.ToString(),
            System.StringComparison.Ordinal
        );
        Assert.Equal(new ObsStreamServer(null, null, null), ObsStreamServer.From(null));
    }
}
