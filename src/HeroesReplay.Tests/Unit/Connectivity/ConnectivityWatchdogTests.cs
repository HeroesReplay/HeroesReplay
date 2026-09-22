using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Connectivity;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.Core.Services.Shared;
using HeroesReplay.Core.Services.Status;
using HeroesReplay.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Connectivity;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ConnectivityWatchdogTests
{
    [Fact]
    public void Apply_DebouncesInternetLossAndRestore()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: false);
        fixture.Probe.Internet = false;

        Assert.False(fixture.Watchdog.Apply(FailSnapshot()));
        Assert.True(fixture.Watchdog.IsOnline);
        Assert.Equal(0, fixture.Obs.StartCalls);
        Assert.Equal(0, fixture.Obs.StopCalls);

        Assert.False(fixture.Watchdog.Apply(FailSnapshot()));
        Assert.True(fixture.Watchdog.Apply(FailSnapshot()));
        Assert.False(fixture.Watchdog.IsOnline);
        Assert.Equal(0, fixture.Obs.StopCalls);

        fixture.Probe.Internet = true;
        Assert.False(fixture.Watchdog.Apply(OkSnapshot()));
        Assert.False(fixture.Watchdog.IsOnline);
        Assert.True(fixture.Watchdog.Apply(OkSnapshot()));
        Assert.True(fixture.Watchdog.IsOnline);
        Assert.Equal(0, fixture.Obs.StartCalls);
    }

    [Fact]
    public void Apply_OfflineThenOnline_ArmsHeroesProfileRetryOnceWithoutStreaming()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: false);
        Assert.False(fixture.Resume.IsPending);

        fixture.Watchdog.Apply(FailSnapshot());
        fixture.Watchdog.Apply(FailSnapshot());
        Assert.False(fixture.Resume.IsPending);
        Assert.True(fixture.Watchdog.IsOnline);

        Assert.True(fixture.Watchdog.Apply(FailSnapshot()));
        Assert.False(fixture.Watchdog.IsOnline);
        Assert.False(fixture.Resume.IsPending);

        Assert.False(fixture.Watchdog.Apply(OkSnapshot()));
        Assert.False(fixture.Resume.IsPending);

        Assert.True(fixture.Watchdog.Apply(OkSnapshot()));
        Assert.True(fixture.Watchdog.IsOnline);
        Assert.True(fixture.Resume.IsPending);
        Assert.True(fixture.Resume.Consume());
        Assert.False(fixture.Resume.IsPending);
        Assert.False(fixture.Resume.Consume());
        Assert.Equal(0, fixture.Obs.StartCalls);
        Assert.Equal(0, fixture.Obs.StopCalls);
    }

    [Fact]
    public void Apply_DoesNotStartStreamWhenStreamingDisabled()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: false);
        DropThenRestore(fixture);
        Assert.Equal(0, fixture.Obs.StartCalls);
        Assert.Equal(0, fixture.Obs.StopCalls);
    }

    [Fact]
    public void Apply_RestartsStreamOnlyWhenStreamingEnabled()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: true);
        DropThenRestore(fixture);
        Assert.Equal(1, fixture.Obs.StopCalls);
        Assert.Equal(1, fixture.Obs.StartCalls);
    }

    [Fact]
    public async Task ProbeAsync_UsesInjectedProbe()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: true);
        fixture.Probe.Internet = true;
        fixture.Probe.Twitch = false;
        fixture.Probe.HeroesProfile = true;

        ConnectivitySnapshot snapshot = await fixture.Watchdog.ProbeAsync(CancellationToken.None);
        Assert.True(snapshot.Internet);
        Assert.True(snapshot.TwitchProbed);
        Assert.False(snapshot.Twitch);
        Assert.True(snapshot.HeroesProfile);
        Assert.False(snapshot.Healthy);
        Assert.Equal(1, fixture.Probe.TwitchCalls);
    }

    [Fact]
    public async Task ProbeAsync_SkipsTwitchWebsiteUnlessStreamingEnabled()
    {
        using Fixture off = CreateFixture(streamingEnabled: false);
        ConnectivitySnapshot skipped = await off.Watchdog.ProbeAsync(CancellationToken.None);
        Assert.Equal(0, off.Probe.TwitchCalls);
        Assert.Equal(1, off.Probe.InternetCalls);
        Assert.Equal(1, off.Probe.HeroesProfileCalls);
        Assert.False(skipped.TwitchProbed);
        Assert.False(skipped.Twitch);
        Assert.True(skipped.Internet);
        Assert.True(skipped.HeroesProfile);
        Assert.True(skipped.Healthy);
        Assert.Contains("twitch=skipped", skipped.Describe(), StringComparison.Ordinal);

        using Fixture on = CreateFixture(streamingEnabled: true);
        on.Probe.Twitch = false;
        ConnectivitySnapshot probed = await on.Watchdog.ProbeAsync(CancellationToken.None);
        Assert.Equal(1, on.Probe.TwitchCalls);
        Assert.True(probed.TwitchProbed);
        Assert.False(probed.Twitch);
        Assert.False(probed.Healthy);
    }

    [Fact]
    public async Task RunAsync_DoesNotPollTwitchWhenStreamingDisabled()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await fixture.Watchdog.RunAsync(cts.Token);
        Assert.Equal(0, fixture.Probe.TwitchCalls);
        Assert.Equal(1, fixture.Probe.InternetCalls);
        Assert.Equal(1, fixture.Probe.HeroesProfileCalls);
    }

    [Fact]
    public async Task RunAsync_ProbesTwitchWhenStreamingEnabled()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await fixture.Watchdog.RunAsync(cts.Token);
        Assert.True(fixture.Probe.TwitchCalls >= 1);
        Assert.True(fixture.Probe.InternetCalls >= 1);
        Assert.True(fixture.Probe.HeroesProfileCalls >= 1);
    }

    [Fact]
    public void AppSettings_StreamingEnabledIsFalse()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        Assert.True(File.Exists(path), $"expected {path} to be copied to the test output.");
        string json = File.ReadAllText(path);
        Assert.Contains("\"StreamingEnabled\": false", json, StringComparison.Ordinal);
    }

    private static void DropThenRestore(Fixture fixture)
    {
        fixture.Watchdog.Apply(FailSnapshot());
        fixture.Watchdog.Apply(FailSnapshot());
        fixture.Watchdog.Apply(FailSnapshot());
        Assert.False(fixture.Watchdog.IsOnline);
        fixture.Watchdog.Apply(OkSnapshot());
        fixture.Watchdog.Apply(OkSnapshot());
        Assert.True(fixture.Watchdog.IsOnline);
    }

    private static ConnectivitySnapshot FailSnapshot() =>
        new()
        {
            Internet = false,
            Twitch = false,
            HeroesProfile = false,
        };

    private static ConnectivitySnapshot OkSnapshot() =>
        new()
        {
            Internet = true,
            Twitch = true,
            HeroesProfile = true,
        };

    private static Fixture CreateFixture(bool streamingEnabled)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"heroesreplay-connectivity-{Guid.NewGuid():N}.json"
        );
        var store = new SpectatorStatusStore(path);
        var probe = new FakeProbe();
        var obs = new FakeObs();
        var resume = new HeroesProfileResume();
        var watchdog = new ConnectivityWatchdog(
            NullLogger<ConnectivityWatchdog>.Instance,
            new AppSettings
            {
                OBS = new OBSSettings { StreamingEnabled = streamingEnabled },
                Connectivity = new ConnectivitySettings
                {
                    FailThreshold = 3,
                    RecoverThreshold = 2,
                    Interval = TimeSpan.FromMilliseconds(1),
                    IdleInterval = TimeSpan.FromMinutes(5),
                },
            },
            probe,
            store,
            new CancellationTokenProvider(),
            obs,
            resume
        );
        return new Fixture(path, probe, obs, watchdog, resume);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(
            string path,
            FakeProbe probe,
            FakeObs obs,
            ConnectivityWatchdog watchdog,
            HeroesProfileResume resume
        )
        {
            Path = path;
            Probe = probe;
            Obs = obs;
            Watchdog = watchdog;
            Resume = resume;
        }

        public string Path { get; }
        public FakeProbe Probe { get; }
        public FakeObs Obs { get; }
        public ConnectivityWatchdog Watchdog { get; }
        public HeroesProfileResume Resume { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }

    private sealed class FakeProbe : INetworkProbe
    {
        public bool Internet { get; set; } = true;
        public bool Twitch { get; set; } = true;
        public bool HeroesProfile { get; set; } = true;
        public int InternetCalls { get; private set; }
        public int TwitchCalls { get; private set; }
        public int HeroesProfileCalls { get; private set; }

        public Task<bool> ProbeInternetAsync(CancellationToken cancellationToken)
        {
            InternetCalls++;
            return Task.FromResult(Internet);
        }

        public Task<bool> ProbeTwitchAsync(CancellationToken cancellationToken)
        {
            TwitchCalls++;
            return Task.FromResult(Twitch);
        }

        public Task<bool> ProbeHeroesProfileAsync(CancellationToken cancellationToken)
        {
            HeroesProfileCalls++;
            return Task.FromResult(HeroesProfile);
        }
    }

    private sealed class FakeObs : IObsController
    {
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public void BeginSession() { }

        public void EndSession() { }

        public void ConfigureFromContext() { }

        public Task CycleReportAsync() => Task.CompletedTask;

        public void SwapToGameScene() { }

        public void SwapToWaitingScene() { }

        public void StartRecording() { }

        public void StopRecording() { }

        public void StartStreaming() => StartCalls++;

        public void StopStreaming() => StopCalls++;

        public bool IsStreaming() => StartCalls > StopCalls;
    }
}
