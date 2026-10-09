using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Tests.Unit.Support;

/// <summary>
/// An in-memory process table (#409): no real process is read, started, or killed. A kill
/// checks the start time as the real table does, and removes the entry.
/// </summary>
internal sealed class FakeProcessTable : IProcessTable
{
    public const string Obs = "obs64.exe";

    private readonly object gate = new();

    public List<ProcessTableEntry> Entries { get; } = new();
    public List<int> Killed { get; } = new();
    public HashSet<int> Unkillable { get; } = new();

    public static ProcessTableEntry Entry(
        int pid,
        int parentPid,
        string name,
        DateTimeOffset? started
    ) => new(pid, parentPid, name, null, started);

    public void Add(ProcessTableEntry entry)
    {
        lock (gate)
        {
            Entries.Add(entry);
        }
    }

    public bool HasObs()
    {
        lock (gate)
        {
            return Entries.Any(entry => entry.Name == Obs);
        }
    }

    public IReadOnlyList<ProcessTableEntry> Snapshot()
    {
        lock (gate)
        {
            return Entries.ToList();
        }
    }

    public ProcessTableEntry Find(int pid)
    {
        lock (gate)
        {
            return Entries.FirstOrDefault(entry => entry.Pid == pid);
        }
    }

    public ProcessKillResult Kill(ProcessTableEntry entry)
    {
        lock (gate)
        {
            ProcessTableEntry now = Entries.FirstOrDefault(each => each.Pid == entry.Pid);
            if (now == null)
            {
                return ProcessKillResult.Gone;
            }

            if (now.StartTime != entry.StartTime)
            {
                return ProcessKillResult.Replaced;
            }

            if (Unkillable.Contains(entry.Pid))
            {
                return ProcessKillResult.AccessDenied;
            }

            Entries.Remove(now);
            Killed.Add(entry.Pid);
            return ProcessKillResult.Killed;
        }
    }
}
