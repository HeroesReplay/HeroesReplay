using System;
using System.IO;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Recording;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class RecordingOwnershipTests
{
    [Fact]
    public void Start_NotConfirmed_DoesNotOwnOrStop()
    {
        var socket = new FakeObsSocket();
        RecordingSession session = Session(socket);

        ObsRecordingResult started = session.StartRecording(Record, Noop, 7, "unit");

        Assert.False(started.Succeeded);
        Assert.False(started.Owned);
        Assert.Equal(ObsOutputFailure.NotConfirmed, started.Failure);
        Assert.Equal(1, socket.StartCalls);
        ObsRecordingResult stopped = session.StopRecording(Noop, 7);
        Assert.Equal(ObsOutputFailure.NotOwned, stopped.Failure);
        Assert.Equal(0, socket.StopCalls);
        Assert.Null(stopped.OutputPath);
    }

    [Fact]
    public void Start_ForeignRecording_StopsItAndOwnsTheNextFile()
    {
        string foreign = Path.Combine(Path.GetTempPath(), "foreign-session.mkv");
        var socket = new FakeObsSocket
        {
            Recording = true,
            RecordingAfterStart = true,
            StopPath = foreign,
        };
        RecordingSession session = Session(socket);

        ObsRecordingResult started = session.StartRecording(Record, Noop, 7, "unit");

        Assert.True(started.Succeeded);
        Assert.True(started.Owned);
        Assert.Null(started.OutputPath);
        Assert.NotEqual(foreign, started.OutputPath);
        Assert.Equal(1, socket.StopCalls);
        Assert.Equal(1, socket.StartCalls);
        ObsRecordingResult stopped = session.StopRecording(Noop, 7);
        Assert.True(stopped.Succeeded);
        Assert.Equal(foreign, stopped.OutputPath);
        Assert.Equal(2, socket.StopCalls);
    }

    [Fact]
    public void Start_ForeignRecordingThatStaysActive_IsNotAdopted()
    {
        var socket = new FakeObsSocket { Recording = true, KeepRecordingOnStop = true };
        RecordingSession session = Session(socket);

        ObsRecordingResult started = session.StartRecording(Record, Noop, 7, "unit");

        Assert.False(started.Succeeded);
        Assert.False(started.Owned);
        Assert.Equal(ObsOutputFailure.AlreadyRecording, started.Failure);
        Assert.Equal(1, socket.StopCalls);
        Assert.Equal(0, socket.StartCalls);
        Assert.Equal(ObsOutputFailure.NotOwned, session.StopRecording(Noop, 7).Failure);
        Assert.Equal(1, socket.StopCalls);
    }

    [Fact]
    public void Start_ForeignRecording_SetsTheDirectoryAfterThatRecordingStops()
    {
        var socket = new FakeObsSocket
        {
            Recording = true,
            RecordingAfterStart = true,
            StopPath = Path.Combine(Path.GetTempPath(), "foreign-session.mkv"),
        };
        RecordingSession session = Session(socket);
        int prepared = 0;

        ObsRecordingResult started = session.StartRecording(
            Record,
            Noop,
            7,
            "unit",
            () =>
            {
                prepared++;
                Assert.Equal(1, socket.StopCalls);
                Assert.Equal(0, socket.StartCalls);
            }
        );

        Assert.True(started.Owned);
        Assert.Equal(1, prepared);
        Assert.Equal(1, socket.StartCalls);
    }

    [Fact]
    public void Start_ForeignRecordingThatStaysActive_DoesNotPrepareTheNextDirectory()
    {
        var socket = new FakeObsSocket { Recording = true, KeepRecordingOnStop = true };
        RecordingSession session = Session(socket);
        int prepared = 0;

        ObsRecordingResult started = session.StartRecording(
            Record,
            Noop,
            7,
            "unit",
            () => prepared++
        );

        Assert.Equal(ObsOutputFailure.AlreadyRecording, started.Failure);
        Assert.Equal(0, prepared);
        Assert.Equal(0, socket.StartCalls);
    }

    [Fact]
    public void Stop_ReturnsThePathObsReported()
    {
        var socket = new FakeObsSocket { RecordingAfterStart = true };
        string finalized = Path.Combine(Path.GetTempPath(), "owned-session.mkv");
        socket.StopPath = finalized;
        RecordingSession session = Session(socket);

        ObsRecordingResult started = session.StartRecording(Record, Noop, 7, "unit");
        ObsRecordingResult stopped = session.StopRecording(Noop, 7);

        Assert.True(started.Owned);
        Assert.True(stopped.Succeeded);
        Assert.True(stopped.Finalized);
        Assert.Equal(finalized, stopped.OutputPath);
        Assert.Equal(1, socket.StopCalls);
    }

    [Fact]
    public void Stop_Timeout_HasNoPath()
    {
        var socket = new FakeObsSocket { RecordingAfterStart = true };
        RecordingSession session = Session(socket);

        Assert.True(session.StartRecording(Record, Noop, 7, "unit").Owned);
        ObsRecordingResult stopped = session.StopRecording(Noop, 7);

        Assert.False(stopped.Succeeded);
        Assert.Equal(ObsOutputFailure.Timeout, stopped.Failure);
        Assert.Null(stopped.OutputPath);
        Assert.False(stopped.Finalized);
    }

    [Fact]
    public void Stop_Timeout_DoesNotMakeTheNextStartLookOwned()
    {
        var socket = new FakeObsSocket { RecordingAfterStart = true, KeepRecordingOnStop = true };
        RecordingSession session = Session(socket);

        Assert.True(session.StartRecording(Record, Noop, 7, "unit").Owned);
        ObsRecordingResult stopped = session.StopRecording(Noop, 7);
        ObsRecordingResult again = session.StartRecording(Record, Noop, 7, "unit");

        Assert.Equal(ObsOutputFailure.Timeout, stopped.Failure);
        Assert.Null(stopped.OutputPath);
        Assert.False(again.Succeeded);
        Assert.False(again.Owned);
        Assert.Equal(ObsOutputFailure.AlreadyRecording, again.Failure);
        Assert.Equal(1, socket.StartCalls);
        Assert.Equal(ObsOutputFailure.NotOwned, session.StopRecording(Noop, 7).Failure);
        Assert.Equal(2, socket.StopCalls);
    }

    [Fact]
    public void Stop_RecordStateChanged_UsesTheStoppedPath()
    {
        string finalized = Path.Combine(Path.GetTempPath(), "from-event.mkv");
        var socket = new FakeObsSocket
        {
            RecordingAfterStart = true,
            OnStop = self => self.Raise(ObsRecordSignal.Stopped(finalized)),
        };
        RecordingSession session = Session(socket);

        Assert.True(session.StartRecording(Record, Noop, 7, "unit").Owned);
        ObsRecordingResult stopped = session.StopRecording(Noop, 7);

        Assert.True(stopped.Succeeded);
        Assert.Equal(finalized, stopped.OutputPath);
    }

    [Fact]
    public void Stop_UsesTheStoppedEventPathFromTheOwnedSession()
    {
        string finalized = Path.Combine(Path.GetTempPath(), "stopped-before-request.mkv");
        var socket = new FakeObsSocket { RecordingAfterStart = true };
        RecordingSession session = Session(socket);
        Assert.True(session.StartRecording(Record, Noop, 7, "unit").Owned);
        socket.Raise(ObsRecordSignal.Stopped(finalized));

        ObsRecordingResult stopped = session.StopRecording(Noop, 7);

        Assert.True(stopped.Succeeded);
        Assert.Equal(finalized, stopped.OutputPath);
        Assert.Equal(0, socket.StopCalls);
    }

    [Fact]
    public void Stop_SplitFile_DoesNotUseTheSplitPath()
    {
        string split = Path.Combine(Path.GetTempPath(), "segment-2.mp4");
        var socket = new FakeObsSocket { RecordingAfterStart = true, StopPath = split };
        RecordingSession session = Session(socket);
        Assert.True(session.StartRecording(Record, Noop, 7, "unit").Owned);
        socket.Raise(ObsRecordSignal.Split(split));

        ObsRecordingResult stopped = session.StopRecording(Noop, 7);

        Assert.False(stopped.Succeeded);
        Assert.Equal(ObsOutputFailure.SplitFile, stopped.Failure);
        Assert.Null(stopped.OutputPath);
        Assert.True(socket.StopCalls > 0);
    }

    [Fact]
    public void Stop_Disconnect_DoesNotGuessAPath()
    {
        var socket = new FakeObsSocket { RecordingAfterStart = true, StopPath = "leftover.mp4" };
        RecordingSession session = Session(socket);
        Assert.True(session.StartRecording(Record, Noop, 7, "unit").Owned);
        socket.Raise(ObsRecordSignal.Disconnected());

        ObsRecordingResult stopped = session.StopRecording(Noop, 7);

        Assert.Equal(ObsOutputFailure.Disconnected, stopped.Failure);
        Assert.Null(stopped.OutputPath);
        Assert.Equal(0, socket.StopCalls);
    }

    [Fact]
    public void Start_RequestError_RetriesThenFails()
    {
        var socket = new FakeObsSocket
        {
            StartError = new InvalidOperationException("socket down"),
        };
        RecordingSession session = Session(socket, FastBudget(retryCount: 1));

        ObsRecordingResult started = session.StartRecording(Record, Noop, 7, "unit");

        Assert.False(started.Succeeded);
        Assert.Equal(ObsOutputFailure.RequestError, started.Failure);
        Assert.Equal(2, socket.StartCalls);
        Assert.Equal(0, socket.StopCalls);
    }

    [Fact]
    public void FinalizedPath_IsNotTheNewestFileInTheDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hr-own-" + Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(directory);
        try
        {
            string finalized = Path.Combine(directory, "owned-earlier.mkv");
            string newest = Path.Combine(directory, "newest.mp4");
            File.WriteAllBytes(finalized, new byte[] { 1 });
            File.WriteAllBytes(newest, new byte[] { 1, 2, 3, 4 });
            File.SetLastWriteTimeUtc(finalized, DateTime.UtcNow.AddMinutes(-5));
            File.SetLastWriteTimeUtc(newest, DateTime.UtcNow);
            var recording = ObsRecordingResult.FinalizedAt(finalized);

            string chosen = RecordingOwnership.SelectFinalizedFile(finalized, directory);
            string discard = RecordingOwnership.FileToDiscard(recording, allowsMedia: false);

            Assert.Equal(finalized, chosen);
            Assert.Equal(finalized, discard);
            Assert.NotEqual(newest, chosen);
            Assert.NotEqual(newest, discard);
            Assert.Null(RecordingOwnership.SelectFinalizedFile(" ", directory));
            Assert.True(RecordingOwnership.CanPublish(recording, allowsMedia: true));
            Assert.False(RecordingOwnership.CanPublish(recording, allowsMedia: false));
            Assert.False(RecordingOwnership.CanPublish(ObsRecordingResult.Started(), true));
            Assert.Null(RecordingOwnership.FileToDiscard(recording, allowsMedia: true));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>#318: the claim services stop reads is written before StartRecord is sent.</summary>
    [Fact]
    public void Claim_WrittenBeforeTheStart_AndDeletedWhenObsFinalizes()
    {
        using var temp = new TempClaim();
        var at = new DateTimeOffset(2026, 10, 8, 12, 33, 26, TimeSpan.Zero);
        var socket = new FakeObsSocket { RecordingAfterStart = true, StopPath = "owned.mp4" };
        bool claimedBeforeStart = false;
        socket.OnStart = () => claimedBeforeStart = temp.Store.TryLoad() != null;
        var session = new RecordingSession(
            NullLogger.Instance,
            socket,
            FastBudget(0),
            temp.Store,
            () => at
        );

        Assert.True(session.StartRecording(Record, Noop, 65820711, "unit").Owned);
        RecordingClaim claim = temp.Store.TryLoad();

        Assert.True(claimedBeforeStart);
        Assert.Equal(65820711, claim.ReplayId);
        Assert.Equal(at, claim.StartedAt);
        Assert.Equal(Environment.ProcessId, claim.ProcessId);
        // #342: the next spectate tells this process from a reused pid by its start time.
        Assert.NotNull(claim.ProcessStartedAt);
        Assert.Equal(ProcessTable.Find(Environment.ProcessId).StartTime, claim.ProcessStartedAt);
        Assert.True(session.StopRecording(Noop, 65820711).Finalized);
        Assert.Null(temp.Store.TryLoad());
        Assert.False(File.Exists(temp.Store.FilePath));
    }

    [Fact]
    public void Claim_StopTimeout_IsKeptForServicesStop()
    {
        using var temp = new TempClaim();
        var socket = new FakeObsSocket { RecordingAfterStart = true, KeepRecordingOnStop = true };
        var session = new RecordingSession(NullLogger.Instance, socket, FastBudget(0), temp.Store);

        Assert.True(session.StartRecording(Record, Noop, 7, "unit").Owned);
        Assert.Equal(ObsOutputFailure.Timeout, session.StopRecording(Noop, 7).Failure);

        Assert.Equal(7, temp.Store.TryLoad()?.ReplayId);
    }

    [Fact]
    public void Claim_UnconfirmedStart_IsKept()
    {
        using var temp = new TempClaim();
        var socket = new FakeObsSocket();
        var session = new RecordingSession(NullLogger.Instance, socket, FastBudget(0), temp.Store);

        ObsRecordingResult started = session.StartRecording(Record, Noop, 7, "unit");

        Assert.Equal(ObsOutputFailure.NotConfirmed, started.Failure);
        Assert.Equal(7, temp.Store.TryLoad()?.ReplayId);
    }

    [Fact]
    public void Claim_StoppedWithoutAPath_IsDeleted()
    {
        using var temp = new TempClaim();
        var socket = new FakeObsSocket
        {
            RecordingAfterStart = true,
            OnStop = self => self.Raise(ObsRecordSignal.Stopped(null)),
        };
        var session = new RecordingSession(NullLogger.Instance, socket, FastBudget(0), temp.Store);

        Assert.True(session.StartRecording(Record, Noop, 7, "unit").Owned);
        Assert.Equal(ObsOutputFailure.NotConfirmed, session.StopRecording(Noop, 7).Failure);

        Assert.Null(temp.Store.TryLoad());
    }

    [Fact]
    public void Claim_NotRequested_WritesNothing()
    {
        using var temp = new TempClaim();
        var socket = new FakeObsSocket { RecordingAfterStart = true };
        var session = new RecordingSession(NullLogger.Instance, socket, FastBudget(0), temp.Store);

        ObsRecordingResult started = session.StartRecording(() => false, Noop, 7, "unit");

        Assert.Equal(ObsOutputFailure.NotRequested, started.Failure);
        Assert.Equal(0, socket.StartCalls);
        Assert.False(File.Exists(temp.Store.FilePath));
    }

    [Fact]
    public void ClaimStore_UnreadableFile_IsMovedAsideAndReadsAsNoClaim()
    {
        using var temp = new TempClaim();
        File.WriteAllText(temp.Store.FilePath, "{ not json");

        Assert.Null(temp.Store.TryLoad());
        Assert.False(File.Exists(temp.Store.FilePath));
        temp.Store.Clear();
    }

    private static bool Record() => true;

    private static void Noop() { }

    private sealed class TempClaim : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            "hr-claim-" + Guid.NewGuid().ToString("n")
        );

        public TempClaim()
        {
            Directory.CreateDirectory(directory);
            Store = new RecordingClaimStore(Path.Combine(directory, "obs-recording.json"));
        }

        public RecordingClaimStore Store { get; }

        public void Dispose()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static RecordingSession Session(FakeObsSocket socket, ObsRecordingBudget budget = null)
    {
        return new RecordingSession(NullLogger.Instance, socket, budget ?? FastBudget(0));
    }

    private static ObsRecordingBudget FastBudget(int retryCount) =>
        new()
        {
            RetryCount = retryCount,
            RetryDelay = TimeSpan.Zero,
            StartTimeout = TimeSpan.FromMilliseconds(40),
            StopTimeout = TimeSpan.FromMilliseconds(40),
            PollInterval = TimeSpan.FromMilliseconds(5),
        };

    private sealed class FakeObsSocket : IObsRecordSocket
    {
        public bool IsIdentified { get; set; } = true;
        public bool IsConnected { get; set; } = true;
        public bool Recording { get; set; }
        public bool RecordingAfterStart { get; set; }
        public bool KeepRecordingOnStop { get; set; }
        public bool Streaming { get; set; }
        public string StopPath { get; set; }
        public Exception StartError { get; set; }
        public Action<FakeObsSocket> OnStop { get; set; }
        public Action OnStart { get; set; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public event EventHandler<ObsRecordSignal> RecordSignal;

        public bool IsRecording() => Recording;

        public bool IsStreamActive() => Streaming;

        public ObsStreamSample ReadStream() => new(Streaming, false, 0);

        public void StartRecord()
        {
            StartCalls++;
            OnStart?.Invoke();
            if (StartError != null)
            {
                throw StartError;
            }

            if (RecordingAfterStart)
            {
                Recording = true;
            }
        }

        public string StopRecord()
        {
            StopCalls++;
            if (!KeepRecordingOnStop)
            {
                Recording = false;
            }

            OnStop?.Invoke(this);
            return StopPath;
        }

        public void StartStream() { }

        public void StopStream() { }

        public void Raise(ObsRecordSignal signal) => RecordSignal?.Invoke(this, signal);
    }
}
