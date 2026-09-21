using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Processes;

namespace HeroesReplay.CLI.Commands.Services;

public static class ServiceSupervisor
{
    public static int Start(
        string lockPath,
        string exePath,
        Func<int, string> processNameOrNull,
        Func<string, string, int?> startProcess
    )
    {
        if (
            string.IsNullOrWhiteSpace(exePath)
            || !ServiceProcessPlan.IsHeroesReplay(Path.GetFileName(exePath))
        )
        {
            Console.Error.WriteLine($"Refusing to start services from `{exePath}`.");
            return 1;
        }

        List<ServiceProcessRecord> living = ServiceProcessPlan.StillRunning(
            ServiceLockStore.TryLoad(lockPath)?.Processes,
            processNameOrNull
        );
        if (living.Count > 0)
        {
            Console.WriteLine("Services already running. Run `heroesreplay services stop` first.");
            foreach (ServiceProcessRecord record in living)
            {
                Console.WriteLine($"  {record.Name} pid {record.Pid} ({record.Arguments})");
            }

            return 1;
        }

        var started = new List<ServiceProcessRecord>();
        foreach ((string name, string arguments) in ServiceProcessPlan.All)
        {
            int? pid = startProcess(name, arguments);
            if (pid is not int id || id <= 0)
            {
                Console.Error.WriteLine($"Failed to start {name} ({arguments}).");
                break;
            }

            started.Add(
                new ServiceProcessRecord
                {
                    Name = name,
                    Pid = id,
                    Arguments = arguments,
                }
            );
            Console.WriteLine($"Started {name} pid {id} ({arguments}).");
        }

        if (started.Count > 0)
        {
            ServiceLockStore.Save(
                lockPath,
                new ServiceLock { StartedAt = DateTimeOffset.UtcNow, Processes = started }
            );
        }

        if (started.Count != ServiceProcessPlan.All.Count)
        {
            return 1;
        }

        Console.WriteLine(
            "Twitch ingest was not started. Streaming stays at OBS:StreamingEnabled."
        );
        return 0;
    }

    public static int Stop(string lockPath, Func<int, string> processNameOrNull, Action<int> kill)
    {
        List<ServiceProcessRecord> living = ServiceProcessPlan.StillRunning(
            ServiceLockStore.TryLoad(lockPath)?.Processes,
            processNameOrNull
        );
        if (living.Count == 0)
        {
            Console.WriteLine("No HeroesReplay services are running.");
            ServiceLockStore.Delete(lockPath);
            return 0;
        }

        foreach (ServiceProcessRecord record in living)
        {
            try
            {
                kill(record.Pid);
                Console.WriteLine($"Stopped {record.Name} pid {record.Pid}.");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(
                    $"Could not stop {record.Name} pid {record.Pid}: {e.Message}"
                );
            }
        }

        ServiceLockStore.Delete(lockPath);
        Console.WriteLine(
            "If Heroes of the Storm is still open, close it. A forced stop does not run the spectator shutdown."
        );
        return 0;
    }

    public static int Status(
        string lockPath,
        Func<int, string> processNameOrNull,
        SpectatorStatus spectator
    )
    {
        ServiceLock snapshot = ServiceLockStore.TryLoad(lockPath);
        int expected = snapshot?.Processes?.Count ?? 0;
        List<ServiceProcessRecord> living = ServiceProcessPlan.StillRunning(
            snapshot?.Processes,
            processNameOrNull
        );
        if (expected == 0)
        {
            Console.WriteLine("Services: not running.");
        }
        else
        {
            Console.WriteLine($"Services: {living.Count} of {expected} still running.");
            foreach (ServiceProcessRecord record in living)
            {
                Console.WriteLine($"  {record.Name} pid {record.Pid} ({record.Arguments})");
            }
        }

        if (spectator == null)
        {
            Console.WriteLine("Spectator status: no snapshot.");
        }
        else
        {
            Console.WriteLine(
                $"Spectator status: phase={spectator.Phase} running={spectator.SpectatorRunning} stale={spectator.SnapshotStale} replay={spectator.ReplayId} map={spectator.Map} timer={spectator.Timer}"
            );
            if (spectator.CompletedReplayId.HasValue)
            {
                Console.WriteLine(
                    $"Last completion: replay {spectator.CompletedReplayId} team {spectator.CompletedWinnerTeam} at {spectator.CompletedAt:O}"
                );
            }
        }

        if (expected == 0)
        {
            return 0;
        }

        return living.Count == expected ? 0 : 1;
    }
}
