using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Telemetry;

public static class ReplaySessionFile
{
    /// <summary>The lock on <see cref="SharedPath"/> that every role shares.</summary>
    public const string SharedMutexName = @"Local\HeroesReplay.ReplaySessions";

    /// <summary>
    /// The newest sessions kept. Every downloaded or spectated replay adds one and nothing removed
    /// them: production had 395 after a week, and the uploader reads them every few seconds. 500
    /// is more than a week of replays, past the 3-day upload window.
    /// </summary>
    public const int MaxSessions = 500;

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
        WithGate(
            file,
            () =>
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
                if (kept.Count > MaxSessions)
                {
                    kept.RemoveRange(0, kept.Count - MaxSessions);
                }

                File.WriteAllLines(file, kept);
            }
        );
    }

    public static string TryRead(int replayId, string path = null)
    {
        if (replayId <= 0)
        {
            return null;
        }

        string found = null;
        string file = path ?? SharedPath;
        WithGate(
            file,
            () =>
            {
                foreach (string line in ReadLines(file))
                {
                    if (
                        HeroesReplayTelemetry.TryParseSession(line, out int id, out _)
                        && id == replayId
                    )
                    {
                        found = line.Trim();
                    }
                }
            }
        );
        return found;
    }

    public static List<int> ReadIds(string path = null)
    {
        var ids = new List<int>();
        var seen = new HashSet<int>();
        string file = path ?? SharedPath;
        WithGate(
            file,
            () =>
            {
                foreach (string line in ReadLines(file))
                {
                    if (
                        HeroesReplayTelemetry.TryParseSession(line, out int id, out _)
                        && seen.Add(id)
                    )
                    {
                        ids.Add(id);
                    }
                }
            }
        );
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

    /// <summary>
    /// The shared file keeps <see cref="SharedMutexName"/>; any other file (a test's temp file)
    /// has a lock of its own, so it never waits on the running stack (#331).
    /// </summary>
    public static string MutexNameFor(string path) =>
        FileMutexName.For(SharedMutexName, SharedPath, path);

    private static void WithGate(string file, Action action)
    {
        using var gate = new Mutex(false, MutexNameFor(file));
        bool held = false;
        try
        {
            try
            {
                held = gate.WaitOne(TimeSpan.FromSeconds(5));
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
                gate.ReleaseMutex();
            }
        }
    }
}
