using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace HeroesReplay.Core.Status;

public static class ReplaySessionFile
{
    private static readonly Mutex Gate = new(false, @"Local\HeroesReplay.ReplaySessions");

    public static string SharedPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "replay-sessions.txt"
        );

    public static Activity Open(int? replayId)
    {
        if (replayId is not int id || id <= 0)
        {
            return HeroesReplayTelemetry.StartSpan("heroesreplay.session");
        }

        string stored = TryRead(id);
        Activity activity = string.IsNullOrWhiteSpace(stored)
            ? HeroesReplayTelemetry.BeginReplaySession(id)
            : HeroesReplayTelemetry.JoinReplaySession(stored, "heroesreplay.session");
        if (activity == null)
        {
            activity = HeroesReplayTelemetry.BeginReplaySession(id);
        }

        if (string.IsNullOrWhiteSpace(stored))
        {
            Publish(activity, id);
        }

        using Activity beat = HeroesReplayTelemetry.StartSpan(
            "heroesreplay.session.joined",
            activity
        );
        HeroesReplayTelemetry.TagReplay(beat, replayId: id);
        return activity;
    }

    public static void Publish(Activity activity, int replayId, string path = null)
    {
        string line = HeroesReplayTelemetry.FormatSession(activity, replayId);
        if (line == null)
        {
            return;
        }

        string file = path ?? SharedPath;
        WithGate(() =>
        {
            string directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var kept = new List<string>();
            if (File.Exists(file))
            {
                foreach (string existing in ReadLines(file))
                {
                    if (
                        HeroesReplayTelemetry.TryParseSession(existing, out int id, out _)
                        && id == replayId
                    )
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(existing))
                    {
                        kept.Add(existing.Trim());
                    }
                }
            }

            kept.Add(line);
            File.WriteAllLines(file, kept);
        });
    }

    public static string TryRead(int replayId, string path = null)
    {
        if (replayId <= 0)
        {
            return null;
        }

        string found = null;
        WithGate(() =>
        {
            foreach (string line in ReadLines(path ?? SharedPath))
            {
                if (
                    HeroesReplayTelemetry.TryParseSession(line, out int id, out _)
                    && id == replayId
                )
                {
                    found = line.Trim();
                }
            }
        });
        return found;
    }

    public static List<int> ReadIds(string path = null)
    {
        var ids = new List<int>();
        WithGate(() =>
        {
            foreach (string line in ReadLines(path ?? SharedPath))
            {
                if (
                    HeroesReplayTelemetry.TryParseSession(line, out int id, out _)
                    && !ids.Contains(id)
                )
                {
                    ids.Add(id);
                }
            }
        });
        return ids;
    }

    public static Activity Join(int replayId, string name, string path = null)
    {
        string stored = TryRead(replayId, path);
        if (stored == null)
        {
            return null;
        }

        return HeroesReplayTelemetry.JoinReplaySession(stored, name);
    }

    private static IEnumerable<string> ReadLines(string file)
    {
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            return Array.Empty<string>();
        }

        try
        {
            return File.ReadAllLines(file);
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static void WithGate(Action action)
    {
        bool held = false;
        try
        {
            try
            {
                held = Gate.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                held = true;
            }

            action();
        }
        finally
        {
            if (held)
            {
                Gate.ReleaseMutex();
            }
        }
    }
}
