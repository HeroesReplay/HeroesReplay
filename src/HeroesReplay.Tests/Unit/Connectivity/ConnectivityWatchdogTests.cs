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
        using Fixture fixture = CreateFixture(streamingEnabled: false);
        fixture.Probe.Internet = true;
        fixture.Probe.Twitch = false;
        fixture.Probe.HeroesProfile = true;

        ConnectivitySnapshot snapshot = await fixture.Watchdog.ProbeAsync(CancellationToken.None);
        Assert.True(snapshot.Internet);
        Assert.False(snapshot.Twitch);
        Assert.True(snapshot.HeroesProfile);
        Assert.False(snapshot.Healthy);
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
                },
            },
            probe,
            store,
            new CancellationTokenProvider(),
            obs
        );
        return new Fixture(path, probe, obs, watchdog);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(string path, FakeProbe probe, FakeObs obs, ConnectivityWatchdog watchdog)
        {
            Path = path;
            Probe = probe;
            Obs = obs;
            Watchdog = watchdog;
        }

        public string Path { get; }
        public FakeProbe Probe { get; }
        public FakeObs Obs { get; }
        public ConnectivityWatchdog Watchdog { get; }

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

        public Task<bool> ProbeInternetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Internet);

        public Task<bool> ProbeTwitchAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Twitch);

        public Task<bool> ProbeHeroesProfileAsync(CancellationToken cancellationToken) =>
            Task.FromResult(HeroesProfile);
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
