using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.Processes;

public sealed class ServiceProcessRecord
{
    public string Name { get; set; }
    public int Pid { get; set; }
    public string Arguments { get; set; }
}

public sealed class ServiceLock
{
    public DateTimeOffset StartedAt { get; set; }
    public List<ServiceProcessRecord> Processes { get; set; } = new();
}

public static class ServiceProcessPlan
{
    public const string ProcessName = "heroesreplay";

    public static IReadOnlyList<(string Name, string Arguments)> All { get; } =
        new (string, string)[]
        {
            ("spectate", "spectate heroesprofile"),
            ("twitch", "twitch connect"),
            ("download", "heroesprofile download"),
            ("youtube", "youtube uploader"),
        };

    public static bool IsHeroesReplay(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        string name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
        return string.Equals(name, ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    public static List<ServiceProcessRecord> StillRunning(
        IEnumerable<ServiceProcessRecord> records,
        Func<int, string> processNameOrNull
    )
    {
        var living = new List<ServiceProcessRecord>();
        if (records == null || processNameOrNull == null)
        {
            return living;
        }

        foreach (ServiceProcessRecord record in records)
        {
            if (record == null || record.Pid <= 0)
            {
                continue;
            }

            if (IsHeroesReplay(processNameOrNull(record.Pid)))
            {
                living.Add(record);
            }
        }

        return living;
    }
}
