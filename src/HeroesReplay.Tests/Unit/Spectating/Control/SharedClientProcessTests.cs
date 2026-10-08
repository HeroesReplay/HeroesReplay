using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroesClientSDK;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Spectating.Clock;
using HeroesReplay.Core.Spectating.Control;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Control;

/// <summary>
/// #382: one HeroesClientSDK client per Heroes process, shared by the match clock, both screen
/// readers and the build check, so the screen readers walk the client's code once per process.
/// The client is served from a fake <see cref="IProcessMemory"/>: a module whose one code section
/// has no pattern, so every discovery walks it and fails, as on a client still unpacking.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class SharedClientProcessTests
{
    private static readonly ClientProcessKey First = new(4120, 638_640_000_000_000_000);

    // A relaunch that Windows gave the same pid (#249): only the start time tells it apart.
    private static readonly ClientProcessKey Relaunch = new(4120, 638_640_000_600_000_000);

    [Fact]
    public void BothScreenReaders_WalkTheCodeOncePerProcess()
    {
        var memory = new FakeClientMemory();
        using var shared = new SharedClientProcess(null, _ => throw new NotSupportedException());
        using var loading = new LoadingScreen();
        using var menus = new ClientScreen();

        for (int pass = 0; pass < 3; pass++)
        {
            shared.Read(First, () => memory.Attach(First), client => loading.Read(client));
            shared.Read(First, () => memory.Attach(First), client => menus.Read(client));
        }

        Assert.Equal(1, memory.CodeWalks);
        Assert.Equal(1, shared.Attaches);
        Assert.Equal(0, shared.Disposed);

        // The next client process: attached again, the old client disposed, one more walk.
        for (int pass = 0; pass < 3; pass++)
        {
            shared.Read(Relaunch, () => memory.Attach(Relaunch), client => loading.Read(client));
            shared.Read(Relaunch, () => memory.Attach(Relaunch), client => menus.Read(client));
        }

        Assert.Equal(2, memory.CodeWalks);
        Assert.Equal(2, shared.Attaches);
        Assert.Equal(1, shared.Disposed);
    }

    [Fact]
    public void ScreenReadersWithTheirOwnClients_WalkTheCodeTwice()
    {
        // What the spectator did before #382: each reader attached the process by itself.
        var memory = new FakeClientMemory();
        using var loading = new LoadingScreen();
        using var menus = new ClientScreen();
        using HeroesClientProcess forLoading = memory.Attach(First);
        using HeroesClientProcess forMenus = memory.Attach(First);

        loading.Read(forLoading);
        menus.Read(forMenus);

        Assert.Equal(2, memory.CodeWalks);
    }

    [Fact]
    public void TheClockAndTheBuildCheck_ReadTheSameClient()
    {
        var memory = new FakeClientMemory();
        using var shared = new SharedClientProcess(null, _ => throw new NotSupportedException());
        using var clock = new MatchClock();
        using var loading = new LoadingScreen();
        HeroesClientProcess seenByClock = null;
        HeroesClientProcess seenByBuild = null;

        MatchClockSample time = shared.Read(
            First,
            () => memory.Attach(First),
            client =>
            {
                seenByClock = client;
                return clock.Read(client);
            }
        );
        HeroesClientVersion build = shared.Read(
            First,
            () => memory.Attach(First),
            client =>
            {
                seenByBuild = client;
                return client.DetectedVersion;
            }
        );
        shared.Read(First, () => memory.Attach(First), client => loading.Read(client));

        Assert.Same(seenByClock, seenByBuild);
        Assert.Equal(1, shared.Attaches);
        Assert.False(time.Ok);
        Assert.Equal(FakeClientMemory.Build, time.ClientVersion);
        Assert.Equal(FakeClientMemory.Build, build);
        Assert.Equal(
            RunningClientBuild.Differs,
            ReplayClientRoute.RunningBuild(build, "2.57.0.98304")
        );
        Assert.Equal(
            RunningClientBuild.Matches,
            ReplayClientRoute.RunningBuild(build, "2.57.0.98348")
        );
    }

    [Fact]
    public void AHandoffDuringARead_DisposesTheOldClientOnlyWhenThatReadEnds()
    {
        var memory = new FakeClientMemory();
        using var shared = new SharedClientProcess(null, _ => throw new NotSupportedException());
        int disposedDuringRead = -1;

        shared.Read(
            First,
            () => memory.Attach(First),
            old =>
            {
                // The switcher handed off to another process while the timer still read the old.
                shared.Read(Relaunch, () => memory.Attach(Relaunch), _ => 0);
                disposedDuringRead = shared.Disposed;
                return old.DetectedVersion;
            }
        );

        Assert.Equal(0, disposedDuringRead);
        Assert.Equal(1, shared.Disposed);
        Assert.Equal(2, shared.Attaches);
    }

    [Fact]
    public void AClientThatDidNotAttach_IsAttachedAgainOnTheNextRead()
    {
        var memory = new FakeClientMemory();
        using var shared = new SharedClientProcess(null, _ => throw new NotSupportedException());
        using var loading = new LoadingScreen();

        // A client that has only just started has no module yet.
        LoadingScreenSample early = shared.Read(
            First,
            () => HeroesClientProcess.Attach(null),
            client => loading.Read(client)
        );
        LoadingScreenSample later = shared.Read(
            First,
            () => memory.Attach(First),
            client => loading.Read(client)
        );
        shared.Read(First, () => memory.Attach(First), client => loading.Read(client));

        Assert.Equal("no-process", early.Reason);
        Assert.Equal("unsupported-build", later.Reason);
        Assert.Equal(2, shared.Attaches);
        Assert.Equal(1, shared.Disposed);
        Assert.Equal(1, memory.CodeWalks);
    }

    [Fact]
    public void WithoutAProcess_TheReadersGetNoClientAndTheOldOneIsDisposed()
    {
        var memory = new FakeClientMemory();
        int attached = 0;
        using var shared = new SharedClientProcess(
            null,
            process =>
            {
                attached++;
                return memory.Attach(ClientProcessKey.Of(process).Value);
            }
        );
        using var clock = new MatchClock();
        using Process self = Process.GetCurrentProcess();

        shared.Read(self, client => clock.Read(client));
        shared.Read(self, client => clock.Read(client));
        MatchClockSample gone = shared.Read(null, client => clock.Read(client));

        Assert.Equal(1, attached);
        Assert.Equal(1, shared.Disposed);
        Assert.Equal("no-process", gone.Reason);
    }

    [Fact]
    public async Task TheTimer_ReadsThroughTheClientTheControllerShares()
    {
        var memory = new FakeClientMemory();
        using var shared = new SharedClientProcess(
            null,
            process => memory.Attach(ClientProcessKey.Of(process).Value)
        );
        using Process self = Process.GetCurrentProcess();
        var timer = new StableGameTimer(new OneProcess(self), shared);
        using var loading = new LoadingScreen();

        GameTimerReading reading = await timer.ReadAsync(CancellationToken.None);
        shared.Read(self, client => loading.Read(client));

        Assert.False(reading.Ok);
        Assert.Equal("memory", reading.Source);
        Assert.Equal(1, shared.Attaches);
    }

    [Fact]
    public void Key_IsThePidAndStartTimeOfALiveProcess()
    {
        using Process self = Process.GetCurrentProcess();

        ClientProcessKey key = ClientProcessKey.Of(self).Value;

        Assert.Equal(Environment.ProcessId, key.Pid);
        Assert.Equal(self.StartTime.ToUniversalTime().Ticks, key.StartTicks);
        Assert.Null(ClientProcessKey.Of(null));
    }

    /// <summary>
    /// A client module with one 4 KB code section and no pattern in it. A walk of the code reads
    /// that section from its start.
    /// </summary>
    private sealed class FakeClientMemory : IProcessMemory
    {
        public const long Base = 0x140000000L;
        public static readonly HeroesClientVersion Build = new(2, 57, 0, 98348);
        private const int CodeRva = 0x1000;
        private const int CodeSize = 0x1000;
        private readonly byte[] image = new byte[0x2000];

        public FakeClientMemory()
        {
            const int pe = 0x80;
            const int table = pe + 24 + 0xF0;
            image[0] = (byte)'M';
            image[1] = (byte)'Z';
            BitConverter.TryWriteBytes(image.AsSpan(0x3C), pe);
            image[pe] = (byte)'P';
            image[pe + 1] = (byte)'E';
            BitConverter.TryWriteBytes(image.AsSpan(pe + 6), (ushort)1);
            BitConverter.TryWriteBytes(image.AsSpan(pe + 20), (ushort)0xF0);
            Encoding.ASCII.GetBytes(".text").CopyTo(image, table);
            BitConverter.TryWriteBytes(image.AsSpan(table + 8), CodeSize);
            BitConverter.TryWriteBytes(image.AsSpan(table + 12), CodeRva);
            BitConverter.TryWriteBytes(image.AsSpan(table + 36), 0x60000020u);
        }

        public int CodeWalks { get; private set; }

        public HeroesClientProcess Attach(ClientProcessKey key) =>
            HeroesClientProcess.FromMemory(
                this,
                new ClientModule(key.Pid, Base, image.Length, Build.ToString(), key.StartTicks)
            );

        public bool TryRead(long address, Span<byte> buffer)
        {
            long offset = address - Base;
            if (offset < 0 || offset + buffer.Length > image.Length)
            {
                return false;
            }

            if (offset == CodeRva && buffer.Length >= CodeSize)
            {
                CodeWalks++;
            }

            image.AsSpan((int)offset, buffer.Length).CopyTo(buffer);
            return true;
        }
    }

    private sealed class OneProcess(Process process) : IGameController
    {
        public Process GetGameProcess() => process;

        public Task<ClientHoldReason> LaunchAsync() => throw new NotSupportedException();

        public Task<bool> StartAuthenticatedReplayAsync(
            string replayPath,
            string replayVersion = null
        ) => throw new NotSupportedException();

        public Task<bool> OpenReplayFromHomeScreenAsync(string replayPath) =>
            throw new NotSupportedException();

        public Task<TimeSpan?> TryReadRunningMatchClockAsync() => throw new NotSupportedException();

        public Task<bool> IsReplayPresentedAsync(LoadedReplay replay) =>
            throw new NotSupportedException();

        public Task<bool> TrySeeEndScreenAsync(bool nearCore) => throw new NotSupportedException();

        public void SendFocus(int player) => throw new NotSupportedException();

        public void SendPanel(Panel panel) => throw new NotSupportedException();

        public void ShowSelectedUnit() => throw new NotSupportedException();

        public void SaveEndScreenshot() => throw new NotSupportedException();

        public bool IsGameHung() => false;

        public bool ReplayFileOpened => false;

        public bool IsGameRunning() => true;

        public void Kill() => throw new NotSupportedException();
    }
}
