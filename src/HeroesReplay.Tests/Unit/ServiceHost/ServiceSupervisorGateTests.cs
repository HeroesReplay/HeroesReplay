using System;
using System.IO;
using System.Threading;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary>
/// From an SSH session the desktop supervisor's <c>Local\</c> mutex is invisible, so
/// <c>services start --supervise</c> and <c>services supervise</c> started a second supervisor,
/// and <c>start --supervise</c> deleted the live supervisor.json (#293). This test process stands
/// in for the supervisor in the other session: supervisor.json names its pid and start time, and
/// the mutex name is one nobody holds.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceSupervisorGateTests
{
    public enum Command
    {
        StartSupervise,
        Supervise,
        Start,
    }

    [Theory]
    [InlineData(Command.StartSupervise)]
    [InlineData(Command.Supervise)]
    [InlineData(Command.Start)]
    public void SupervisorInAnotherSession_RefusesAndLeavesSupervisorJson(Command command)
    {
        using var other = new OtherSession(updatedAgo: TimeSpan.FromSeconds(10));

        bool proceeded = other.Run(command, out ServiceSupervisorMutex claim);

        Assert.False(proceeded);
        Assert.Null(claim);
        Assert.Equal(
            $"supervisor already running: pid {Environment.ProcessId}, seen via supervisor.json"
                + Environment.NewLine
                + "Run `heroesreplay services stop` first; it stops the roles and the supervisor."
                + Environment.NewLine,
            other.Error.ToString()
        );
        Assert.Equal(other.Written, File.ReadAllBytes(other.StatePath));
        Assert.Equal(other.WrittenAt, File.GetLastWriteTimeUtc(other.StatePath));
        // The check ran before the mutex was taken: this session never created it.
        Assert.False(Mutex.TryOpenExisting(other.MutexName, out _));
    }

    [Theory]
    [InlineData(Command.StartSupervise)]
    [InlineData(Command.Supervise)]
    [InlineData(Command.Start)]
    public void StaleSupervisorJson_Proceeds(Command command)
    {
        // The pid and its start time still match, but a supervisor writes at least every
        // heartbeat interval: a file 10 minutes old is a dead or hung one's.
        using var other = new OtherSession(updatedAgo: TimeSpan.FromMinutes(10));

        bool proceeded = other.Run(command, out ServiceSupervisorMutex claim);
        using (claim)
        {
            Assert.True(proceeded);
            Assert.Equal(string.Empty, other.Error.ToString());
            Assert.Equal(command != Command.Start, claim != null);
            // `services start` removes a dead supervisor's file, as before #293. `supervise`
            // leaves it for the new supervisor to overwrite.
            Assert.Equal(command == Command.Supervise, File.Exists(other.StatePath));
        }
    }

    [Fact]
    public void SupervisorInThisSession_RefusesBySeeingItsMutex()
    {
        using var other = new OtherSession(updatedAgo: TimeSpan.FromSeconds(10));
        using ServiceSupervisorMutex held = ServiceSupervisorMutex.TryAcquire(other.MutexName);

        Assert.Null(other.Gate.TryClaim());
        Assert.StartsWith(
            $"supervisor already running: pid {Environment.ProcessId}, seen via its mutex"
                + Environment.NewLine,
            other.Error.ToString(),
            StringComparison.Ordinal
        );
        Assert.Equal(other.Written, File.ReadAllBytes(other.StatePath));
    }

    /// <summary>
    /// A fresh or stale supervisor.json for this process, as a supervisor in another logon
    /// session writes it, and a gate whose mutex this session cannot see.
    /// </summary>
    private sealed class OtherSession : IDisposable
    {
        public OtherSession(TimeSpan updatedAgo)
        {
            MutexName =
                @"Local\HeroesReplay.ServiceSupervisor.Test." + Guid.NewGuid().ToString("N");
            StatePath = Path.Combine(
                Path.GetTempPath(),
                $"heroesreplay-supervisor-{Guid.NewGuid():N}.json"
            );
            DateTimeOffset started = ProcessTable.Find(Environment.ProcessId).StartTime.Value;
            ServiceSupervisorFile.Save(
                StatePath,
                new ServiceSupervisorState
                {
                    Pid = Environment.ProcessId,
                    ProcessStartedAt = started,
                    StartedAt = started + TimeSpan.FromSeconds(1),
                    UpdatedAt = DateTimeOffset.UtcNow - updatedAgo,
                    Budget = 5,
                }
            );
            Written = File.ReadAllBytes(StatePath);
            WrittenAt = File.GetLastWriteTimeUtc(StatePath);
            Gate = new ServiceSupervisorGate
            {
                MutexName = MutexName,
                StatePath = StatePath,
                Error = Error,
            };
        }

        public string MutexName { get; }
        public string StatePath { get; }
        public byte[] Written { get; }
        public DateTime WrittenAt { get; }
        public StringWriter Error { get; } = new();
        public ServiceSupervisorGate Gate { get; }

        public bool Run(Command command, out ServiceSupervisorMutex claim)
        {
            switch (command)
            {
                case Command.StartSupervise:
                    return Gate.TryStart(supervise: true, out claim);
                case Command.Start:
                    return Gate.TryStart(supervise: false, out claim);
                default:
                    claim = Gate.TryClaim();
                    return claim != null;
            }
        }

        public void Dispose() => ServiceSupervisorFile.Delete(StatePath);
    }
}
