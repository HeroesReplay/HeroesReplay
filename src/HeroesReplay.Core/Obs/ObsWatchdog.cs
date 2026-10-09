using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// The OBS watchdog's rules (#398), from <c>ServiceRestart</c> and <c>OBS:HungAfter</c>
/// (<c>ServiceRestartSettings.ObsRules</c>).
/// </summary>
public sealed record ObsWatchdogRules
{
    public const int DefaultBudget = 4;
    public static readonly TimeSpan DefaultHungAfter = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan DefaultCloseWait = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan DefaultProbeInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(30);

    /// <summary>The waits before the second, third, ... start in the window. The first is at once.</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultBackoff = new[]
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
    };

    /// <summary><c>ServiceRestart:ObsWatchdog</c>.</summary>
    public bool Enabled { get; init; }
    public TimeSpan HungAfter { get; init; } = DefaultHungAfter;
    public IReadOnlyList<TimeSpan> Backoff { get; init; } = DefaultBackoff;
    public int Budget { get; init; } = DefaultBudget;
    public TimeSpan Window { get; init; } = DefaultWindow;
    public TimeSpan CloseWait { get; init; } = DefaultCloseWait;
    public TimeSpan ProbeInterval { get; init; } = DefaultProbeInterval;

    /// <summary>obs64.exe (<c>OBS:ExecutablePath</c>, the registry, or Program Files).</summary>
    public string ExecutablePath { get; init; }

    /// <summary>The profile and collection only; never <c>--startstreaming</c>.</summary>
    public string Arguments { get; init; }

    /// <summary>0 for the first start in the window, then the backoff; the last delay repeats.</summary>
    public TimeSpan DelayFor(int startsInWindow)
    {
        if (startsInWindow <= 0)
        {
            return TimeSpan.Zero;
        }

        IReadOnlyList<TimeSpan> delays = Backoff is { Count: > 0 } ? Backoff : DefaultBackoff;
        return delays[Math.Min(startsInWindow - 1, delays.Count - 1)];
    }
}

/// <summary>What <see cref="ObsWatchdogState.State"/> says.</summary>
public static class ObsWatchdogStates
{
    /// <summary><c>ServiceRestart:ObsWatchdog</c> is false.</summary>
    public const string Off = "off";

    /// <summary>Streaming is not desired here, so OBS is not watched.</summary>
    public const string NotDesired = "not_desired";

    /// <summary>The supervisor cannot start OBS where it would be visible (session 0).</summary>
    public const string Suppressed = "suppressed";

    /// <summary><c>services stop</c> is under way: nothing is started or restarted.</summary>
    public const string Stopping = "stopping";

    /// <summary>OBS runs and its websocket answered inside <c>OBS:HungAfter</c>.</summary>
    public const string Running = "running";

    /// <summary>OBS runs and has not answered yet, still inside <c>OBS:HungAfter</c>.</summary>
    public const string Waiting = "waiting";

    /// <summary>No obs64 process: it is started, after its backoff.</summary>
    public const string Missing = "missing";

    /// <summary>OBS runs, but its websocket has not answered for <c>OBS:HungAfter</c> and the stream is not live.</summary>
    public const string Hung = "hung";

    /// <summary>The watchdog is closing, killing, and starting OBS now.</summary>
    public const string Restarting = "restarting";

    /// <summary>The watchdog used its budget and stops trying until the supervisor restarts.</summary>
    public const string Exhausted = "exhausted";
}

/// <summary>
/// The watchdog's state, written to <c>supervisor.json</c> as <c>obs</c> and shown by
/// <c>services status</c>. Spectate's stream hold (#396) can read <see cref="State"/>.
/// </summary>
public sealed class ObsWatchdogState
{
    public const string ProcessMissingCode = "obs.process_missing";
    public const string WebsocketHungCode = "obs.websocket_hung";
    public const string ExhaustedCode = "obs.watchdog_exhausted";

    public bool Enabled { get; set; }
    public bool Desired { get; set; }

    /// <summary>Why OBS is not watched, when it is not (<see cref="ObsWatchdogStates"/>).</summary>
    public string Reason { get; set; }

    /// <summary><see cref="ObsWatchdogStates"/>.</summary>
    public string State { get; set; } = ObsWatchdogStates.Off;
    public int? Pid { get; set; }
    public DateTimeOffset? RunningSince { get; set; }

    /// <summary>When this OBS process last answered the watchdog's websocket probe.</summary>
    public DateTimeOffset? LastAnswerAt { get; set; }

    /// <summary>The stream as the last probe read it (<see cref="ObsStreamState"/>).</summary>
    public string Stream { get; set; }
    public DateTimeOffset? CheckedAt { get; set; }

    /// <summary>OBS starts and restarts this supervisor made.</summary>
    public int Restarts { get; set; }

    /// <summary>The starts inside the budget window, oldest first.</summary>
    public List<DateTimeOffset> Recent { get; set; } = new();
    public DateTimeOffset? LastRestartAt { get; set; }

    /// <summary><see cref="ProcessMissingCode"/> or <see cref="WebsocketHungCode"/>.</summary>
    public string LastCause { get; set; }
    public string LastDetail { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public bool Exhausted { get; set; }
    public DateTimeOffset? ExhaustedAt { get; set; }
    public int Budget { get; set; }
    public long BudgetWindowSeconds { get; set; }
    public long HungAfterSeconds { get; set; }

    /// <summary>
    /// The <c>services status</c> line, for example <c>running, pid 252, websocket answered 12s
    /// ago, stream Inactive; restarts 0 of 4 in 30m.</c>
    /// </summary>
    public string Describe(DateTimeOffset now)
    {
        if (
            State
                is ObsWatchdogStates.Off
                    or ObsWatchdogStates.NotDesired
                    or ObsWatchdogStates.Stopping
            || Budget <= 0
        )
        {
            // Not watching: no budget to show (#398: "off; restarts 0 of 0 in 0s" on ASA-SERVER).
            string why = string.IsNullOrWhiteSpace(Reason) ? string.Empty : " " + Reason;
            return $"{State}.{why}";
        }

        string window = Span(TimeSpan.FromSeconds(BudgetWindowSeconds));
        int used = Recent?.Count(at => now - at < TimeSpan.FromSeconds(BudgetWindowSeconds)) ?? 0;
        string restarts = Exhausted
            ? $"restarts {Restarts}, budget of {Budget} in {window} exhausted [{ExhaustedCode}]"
            : $"restarts {used} of {Budget} in {window}";
        string last = LastRestartAt is DateTimeOffset at
            ? $", last {Span(now - at)} ago ({LastCause})"
            : string.Empty;
        var facts = new List<string> { State };
        if (Pid is int pid)
        {
            facts.Add("pid " + pid);
        }

        if (
            State
            is ObsWatchdogStates.Running
                or ObsWatchdogStates.Waiting
                or ObsWatchdogStates.Hung
        )
        {
            facts.Add(
                LastAnswerAt is DateTimeOffset answer
                    ? $"websocket answered {Span(now - answer)} ago"
                    : "websocket has not answered yet"
            );
        }

        if (!string.IsNullOrWhiteSpace(Stream))
        {
            facts.Add("stream " + Stream);
        }

        if (NextAttemptAt is DateTimeOffset next)
        {
            facts.Add($"next start in {Span(next - now)}");
        }

        string reason = string.IsNullOrWhiteSpace(Reason) ? string.Empty : " " + Reason;
        return $"{string.Join(", ", facts)}; {restarts}{last}.{reason}";
    }

    private static string Span(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalSeconds < 60)
        {
            return $"{(int)span.TotalSeconds}s";
        }

        if (span.TotalMinutes < 60)
        {
            return span.Seconds == 0
                ? $"{(int)span.TotalMinutes}m"
                : $"{(int)span.TotalMinutes}m {span.Seconds}s";
        }

        return span.Minutes == 0
            ? $"{(int)span.TotalHours}h"
            : $"{(int)span.TotalHours}h {span.Minutes}m";
    }
}

public enum ObsWatchdogAction
{
    None,

    /// <summary>No obs64 runs: start it.</summary>
    Start,

    /// <summary>OBS hangs: close it, kill it if it stays, and start it.</summary>
    Restart,

    /// <summary>OBS needs a start, but the budget is used: log one error and stop trying.</summary>
    Exhausted,
}

/// <summary>Whether streaming is desired here, and why not when it is not.</summary>
public sealed record ObsWatchdogDesire(bool Desired, string Reason)
{
    public static ObsWatchdogDesire Yes { get; } = new(true, null);

    public static ObsWatchdogDesire No(string reason) => new(false, reason);
}

/// <summary>One obs64 process.</summary>
public sealed record ObsProcessInfo(int Pid, DateTimeOffset? StartedAt);

public enum ObsProbeOutcome
{
    /// <summary>OBS identified the websocket (it may still have refused the password).</summary>
    Answered,

    /// <summary>No identify, or the request timed out.</summary>
    NoAnswer,

    /// <summary>No endpoint is configured, so the websocket cannot be checked.</summary>
    Unconfigured,
}

/// <summary>One watchdog probe: identify, then <c>GetStreamStatus</c> (null when it was not read).</summary>
public sealed record ObsWatchdogProbe(ObsProbeOutcome Outcome, JObject StreamStatus, string Detail)
{
    public static ObsWatchdogProbe Answered(JObject streamStatus, string detail = null) =>
        new(ObsProbeOutcome.Answered, streamStatus, detail);

    public static ObsWatchdogProbe NoAnswer(string detail) =>
        new(ObsProbeOutcome.NoAnswer, null, detail);

    public static ObsWatchdogProbe Unconfigured(string detail) =>
        new(ObsProbeOutcome.Unconfigured, null, detail);
}

/// <summary>What one watchdog pass saw.</summary>
public sealed record ObsWatchdogObservation
{
    public ObsWatchdogDesire Desire { get; init; } = ObsWatchdogDesire.No("not read");
    public bool StopRequested { get; init; }

    /// <summary>Null when the supervisor may start OBS here; else why it may not.</summary>
    public string CannotControl { get; init; }
    public ObsProcessInfo Obs { get; init; }
    public DateTimeOffset? LastAnswerAt { get; init; }

    /// <summary>False when no endpoint is configured: a silent websocket is then not a hang.</summary>
    public bool CanProbe { get; init; } = true;
    public ObsStreamHealth Stream { get; init; }

    /// <summary>When this supervisor began watching: a hang is counted from then at the earliest.</summary>
    public DateTimeOffset WatchingSince { get; init; }
}

/// <summary>
/// The watchdog's rules, pure so a fake clock drives them (#398). Only while streaming is
/// desired and no stop is under way: a missing obs64 is started (at once, then after 1, 2, 5,
/// 10 min); a running OBS whose websocket has not answered for <c>OBS:HungAfter</c>, counted
/// from its start, the last answer, or the watch, whichever is latest, is restarted unless its
/// stream is live. A stream whose bytes advance is never touched. Every start counts against
/// the budget; with none left the watchdog stops trying.
/// </summary>
public static class ObsWatchdogPolicy
{
    public static ObsWatchdogAction Decide(
        ObsWatchdogObservation seen,
        ObsWatchdogState state,
        DateTimeOffset now,
        ObsWatchdogRules rules
    )
    {
        ArgumentNullException.ThrowIfNull(seen);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rules);
        state.Recent ??= new List<DateTimeOffset>();
        state.Recent.RemoveAll(at => now - at >= rules.Window);
        state.Enabled = rules.Enabled;
        state.Desired = seen.Desire?.Desired == true;
        state.Budget = rules.Budget;
        state.BudgetWindowSeconds = (long)rules.Window.TotalSeconds;
        state.HungAfterSeconds = (long)rules.HungAfter.TotalSeconds;
        state.Pid = seen.Obs?.Pid;
        state.RunningSince = seen.Obs?.StartedAt;
        state.Stream = seen.Obs == null ? null : seen.Stream?.State.ToString();
        state.Reason = null;
        if (!rules.Enabled)
        {
            return Idle(state, ObsWatchdogStates.Off, "ServiceRestart:ObsWatchdog is false.");
        }

        if (seen.StopRequested)
        {
            // services stop never makes the supervisor start or restart OBS.
            return Idle(state, ObsWatchdogStates.Stopping, "A stop was requested.");
        }

        if (!state.Desired)
        {
            return Idle(state, ObsWatchdogStates.NotDesired, seen.Desire?.Reason);
        }

        if (state.Exhausted)
        {
            state.State = ObsWatchdogStates.Exhausted;
            state.NextAttemptAt = null;
            return ObsWatchdogAction.None;
        }

        if (seen.Obs == null)
        {
            state.State = ObsWatchdogStates.Missing;
            return Due(seen, state, now, rules, ObsWatchdogAction.Start);
        }

        DateTimeOffset since = Latest(seen.WatchingSince, seen.Obs.StartedAt, seen.LastAnswerAt);
        bool answered = seen.LastAnswerAt is DateTimeOffset answer && answer >= since;
        if (!seen.CanProbe || now - since < rules.HungAfter)
        {
            state.State = answered ? ObsWatchdogStates.Running : ObsWatchdogStates.Waiting;
            state.NextAttemptAt = null;
            return ObsWatchdogAction.None;
        }

        if (seen.Stream?.IsLive == true)
        {
            // Never kill OBS while its stream bytes still advance.
            state.State = ObsWatchdogStates.Running;
            state.Reason = "The websocket is silent, but the stream is live.";
            state.NextAttemptAt = null;
            return ObsWatchdogAction.None;
        }

        state.State = ObsWatchdogStates.Hung;
        return Due(seen, state, now, rules, ObsWatchdogAction.Restart);
    }

    /// <summary>One start or restart began at <paramref name="at"/>. It counts against the budget.</summary>
    public static void Started(
        ObsWatchdogState state,
        DateTimeOffset at,
        string cause,
        string detail
    )
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Restarts++;
        state.Recent ??= new List<DateTimeOffset>();
        state.Recent.Add(at);
        state.LastRestartAt = at;
        state.LastCause = cause;
        state.LastDetail = detail;
        state.NextAttemptAt = null;
    }

    private static ObsWatchdogAction Due(
        ObsWatchdogObservation seen,
        ObsWatchdogState state,
        DateTimeOffset now,
        ObsWatchdogRules rules,
        ObsWatchdogAction action
    )
    {
        if (seen.CannotControl != null)
        {
            state.State = ObsWatchdogStates.Suppressed;
            state.Reason = seen.CannotControl;
            state.NextAttemptAt = null;
            return ObsWatchdogAction.None;
        }

        if (state.Recent.Count >= rules.Budget)
        {
            state.Exhausted = true;
            state.ExhaustedAt = now;
            state.NextAttemptAt = null;
            return ObsWatchdogAction.Exhausted;
        }

        // The backoff runs from the last start: a start that did not stay waits the next delay.
        state.NextAttemptAt ??= (state.LastRestartAt ?? now) + rules.DelayFor(state.Recent.Count);
        return now >= state.NextAttemptAt ? action : ObsWatchdogAction.None;
    }

    private static ObsWatchdogAction Idle(ObsWatchdogState state, string name, string reason)
    {
        state.State = name;
        state.Reason = reason;
        state.NextAttemptAt = null;
        return ObsWatchdogAction.None;
    }

    private static DateTimeOffset Latest(params DateTimeOffset?[] values) =>
        values.Where(value => value.HasValue).Select(value => value.Value).DefaultIfEmpty().Max();
}

/// <summary>
/// The supervisor's OBS watchdog (#398). Each pass it finds obs64, asks its websocket for the
/// stream status every <see cref="ObsWatchdogRules.ProbeInterval"/> (its own short read-only
/// session, never the spectator's), applies <see cref="ObsWatchdogPolicy"/>, and starts or
/// restarts OBS: a hung OBS gets <c>CloseMainWindow</c>, is killed when it is still there after
/// <see cref="ObsWatchdogRules.CloseWait"/>, and is started again with the profile and the
/// collection, after its stale crash sentinel is removed. It never starts the stream: spectate's
/// guarded <c>ReconcileStream</c> does. Every step that touches OBS is a port, so tests use
/// fakes.
/// </summary>
public sealed class ObsWatchdog
{
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(5);
    private ObsStreamHealth stream;
    private DateTimeOffset? lastProbeAt;
    private DateTimeOffset? lastAnswerAt;
    private int? probedPid;
    private bool canProbe = true;
    private string lastLoggedState;
    private string lastLoggedReason;

    public ObsWatchdogRules Rules { get; init; } = new();
    public ObsWatchdogState State { get; } = new();
    public DateTimeOffset WatchingSince { get; init; }

    public Func<ObsWatchdogDesire> Desired { get; init; }

    /// <summary>Null when the supervisor may start and stop OBS here; else why not (session 0).</summary>
    public Func<string> CannotControl { get; init; }

    /// <summary>The obs64 process, or null when none runs.</summary>
    public Func<ObsProcessInfo> FindObs { get; init; }
    public Func<ObsWatchdogProbe> Probe { get; init; }

    /// <summary>
    /// True when spectate's fresh <c>status.json</c> says the stream is live (#395's
    /// <c>obsStreamState</c>, read over spectate's own connection). Null reads nothing.
    /// </summary>
    public Func<bool> SpectatorSeesLiveStream { get; init; }

    /// <summary>Asks the process to close (<c>CloseMainWindow</c>).</summary>
    public Action<int> Close { get; init; }

    /// <summary>True once the pid is gone; waits at most the given time.</summary>
    public Func<int, TimeSpan, bool> WaitForExit { get; init; }
    public Action<int> Kill { get; init; }

    /// <summary>Starts OBS (path, arguments) detached from the supervisor. Null on success, else why not.</summary>
    public Func<string, string, string> Start { get; init; }

    /// <summary>Deletes OBS's stale crash sentinels before a start (<see cref="ObsCrashSentinel"/>).</summary>
    public Action RemoveStaleSentinels { get; init; }

    /// <summary>Called before a slow step, so <c>supervisor.json</c> shows the state.</summary>
    public Action Changed { get; set; }
    public ILogger Logger { get; init; } = NullLogger.Instance;

    /// <summary>One pass. Never throws.</summary>
    public ObsWatchdogAction Tick(DateTimeOffset now, bool stopRequested)
    {
        try
        {
            ObsWatchdogObservation seen = Observe(now, stopRequested);
            ObsWatchdogAction action = ObsWatchdogPolicy.Decide(seen, State, now, Rules);
            State.LastAnswerAt = seen.Obs == null ? null : lastAnswerAt;
            State.CheckedAt = lastProbeAt;
            LogState();
            switch (action)
            {
                case ObsWatchdogAction.Start:
                    StartObs(now, ObsWatchdogState.ProcessMissingCode, "OBS is not running.");
                    break;
                case ObsWatchdogAction.Restart:
                    RestartHung(seen, now);
                    break;
                case ObsWatchdogAction.Exhausted:
                    Logger.LogError(
                        "OBS needs a start, but the watchdog already started it {Budget} times in {Window}, so it stops trying [{Code}]. Last cause: {Cause}. Fix OBS, then restart the supervisor (Ctrl+C in its console, then `heroesreplay services supervise`).",
                        Rules.Budget,
                        Describe(Rules.Window),
                        ObsWatchdogState.ExhaustedCode,
                        State.LastCause ?? "none"
                    );
                    break;
            }

            return action;
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "The OBS watchdog pass failed.");
            return ObsWatchdogAction.None;
        }
    }

    private ObsWatchdogObservation Observe(DateTimeOffset now, bool stopRequested)
    {
        ObsWatchdogDesire desire = Desired?.Invoke() ?? ObsWatchdogDesire.No("not read");
        ObsProcessInfo obs = FindObs?.Invoke();
        if (obs?.Pid != probedPid)
        {
            // A new process starts its own clock: its answers, its stream.
            probedPid = obs?.Pid;
            lastAnswerAt = null;
            lastProbeAt = null;
            stream = null;
        }

        if (
            obs != null
            && Rules.Enabled
            && desire.Desired
            && !stopRequested
            && (lastProbeAt == null || now - lastProbeAt >= Rules.ProbeInterval)
        )
        {
            ProbeObs(now);
        }

        // Spectate reads the stream over its own connection. When it sees the stream live, the
        // bytes advance, and OBS is never killed, whatever this watchdog's own probe got.
        ObsStreamHealth seenStream = stream;
        if (obs != null && seenStream?.IsLive != true && SpectatorSeesLive())
        {
            seenStream = new ObsStreamHealth
            {
                State = ObsStreamState.Live,
                At = now,
                Detail = "Spectate reads the stream live over its own connection (status.json).",
            };
        }

        return new ObsWatchdogObservation
        {
            Desire = desire,
            StopRequested = stopRequested,
            CannotControl = CannotControl?.Invoke(),
            Obs = obs,
            LastAnswerAt = lastAnswerAt,
            CanProbe = canProbe,
            Stream = seenStream,
            WatchingSince = WatchingSince,
        };
    }

    private bool SpectatorSeesLive()
    {
        try
        {
            return SpectatorSeesLiveStream?.Invoke() == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ProbeObs(DateTimeOffset now)
    {
        lastProbeAt = now;
        ObsWatchdogProbe probe;
        try
        {
            probe = Probe?.Invoke() ?? ObsWatchdogProbe.Unconfigured("No probe was given.");
        }
        catch (Exception e)
        {
            probe = ObsWatchdogProbe.NoAnswer(e.Message);
        }

        canProbe = probe.Outcome != ObsProbeOutcome.Unconfigured;
        if (probe.Outcome == ObsProbeOutcome.Answered)
        {
            bool first = lastAnswerAt == null;
            lastAnswerAt = now;
            stream =
                probe.StreamStatus == null
                    ? ObsStreamHealth.Unknown(stream, probe.Detail, now)
                    : ObsStreamHealth.Next(stream, probe.StreamStatus, now);
            if (first && State.LastRestartAt is DateTimeOffset started && probedPid != null)
            {
                Logger.LogInformation(
                    "OBS pid {Pid} answered its websocket {After} after the watchdog acted (a hung OBS's close wait included). Stream: {Stream}.",
                    probedPid,
                    Describe(now - started),
                    stream.State
                );
            }

            return;
        }

        stream = ObsStreamHealth.Unknown(stream, probe.Detail, now);
    }

    private void StartObs(DateTimeOffset now, string cause, string why)
    {
        int attempt = State.Recent.Count + 1;
        Logger.LogWarning(
            "{Why} Starting it [{Code}]: `{Path}` {Arguments}, attempt {Attempt} of {Budget} in {Window}. Spectate starts the stream, never the watchdog.",
            why,
            cause,
            Rules.ExecutablePath,
            Rules.Arguments,
            attempt,
            Rules.Budget,
            Describe(Rules.Window)
        );
        ObsWatchdogPolicy.Started(State, now, cause, why);
        Safely(() => RemoveStaleSentinels?.Invoke(), "remove OBS's stale crash sentinels");
        string failure;
        try
        {
            failure =
                Start == null
                    ? "No start step was given."
                    : Start(Rules.ExecutablePath, Rules.Arguments);
        }
        catch (Exception e)
        {
            failure = e.Message;
        }

        if (failure != null)
        {
            State.LastDetail = why + " It did not start: " + failure;
            Logger.LogWarning("OBS did not start: {Failure}", failure);
        }
    }

    private void RestartHung(ObsWatchdogObservation seen, DateTimeOffset now)
    {
        int pid = seen.Obs.Pid;
        DateTimeOffset since = new[] { seen.WatchingSince, seen.Obs.StartedAt, seen.LastAnswerAt }
            .Where(value => value.HasValue)
            .Select(value => value.Value)
            .Max();
        string why =
            $"OBS pid {pid} has not answered its websocket for {Describe(now - since)} (limit {Describe(Rules.HungAfter)}), and its stream is {StreamName(seen.Stream)}.";
        Logger.LogWarning(
            "{Why} Closing it, killing it if it is still there after {Wait}, then starting it again [{Code}].",
            why,
            Describe(Rules.CloseWait),
            ObsWatchdogState.WebsocketHungCode
        );
        State.State = ObsWatchdogStates.Restarting;
        Safely(() => Changed?.Invoke(), "save the supervisor state");
        Safely(() => Close?.Invoke(pid), "ask OBS to close");
        bool gone = Wait(pid, Rules.CloseWait);
        if (!gone)
        {
            Logger.LogWarning(
                "OBS pid {Pid} was still running {Wait} after CloseMainWindow. Killing it.",
                pid,
                Describe(Rules.CloseWait)
            );
            Safely(() => Kill?.Invoke(pid), "kill OBS");
            gone = Wait(pid, KillWait);
        }

        if (!gone)
        {
            ObsWatchdogPolicy.Started(
                State,
                now,
                ObsWatchdogState.WebsocketHungCode,
                why + " It did not exit, so no second OBS was started."
            );
            Logger.LogWarning(
                "OBS pid {Pid} did not exit, so the watchdog did not start a second OBS. It tries again after its backoff.",
                pid
            );
            return;
        }

        StartObs(now, ObsWatchdogState.WebsocketHungCode, why);
    }

    private bool Wait(int pid, TimeSpan timeout)
    {
        try
        {
            return WaitForExit?.Invoke(pid, timeout) ?? false;
        }
        catch (Exception e)
        {
            Logger.LogWarning("Could not wait for OBS pid {Pid}: {Message}", pid, e.Message);
            return false;
        }
    }

    private void Safely(Action step, string what)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            Logger.LogWarning("The OBS watchdog could not {What}: {Message}", what, e.Message);
        }
    }

    /// <summary>One line when the watchdog stops or starts watching, and why.</summary>
    private void LogState()
    {
        string state = State.State;
        bool watching =
            state
            is ObsWatchdogStates.Running
                or ObsWatchdogStates.Waiting
                or ObsWatchdogStates.Missing
                or ObsWatchdogStates.Hung;
        string key = watching ? "watching" : state;
        if (key == lastLoggedState && State.Reason == lastLoggedReason)
        {
            return;
        }

        if (key == lastLoggedState && watching)
        {
            return;
        }

        lastLoggedState = key;
        lastLoggedReason = State.Reason;
        if (watching)
        {
            Logger.LogInformation(
                "The OBS watchdog watches OBS: a missing obs64 is started, and one whose websocket does not answer for {HungAfter} while its stream is not live is restarted. Budget {Budget} starts in {Window}.",
                Describe(Rules.HungAfter),
                Rules.Budget,
                Describe(Rules.Window)
            );
        }
        else if (state == ObsWatchdogStates.Suppressed)
        {
            Logger.LogWarning("The OBS watchdog cannot start OBS here: {Reason}", State.Reason);
        }
        else if (state != ObsWatchdogStates.Exhausted && state != ObsWatchdogStates.Stopping)
        {
            Logger.LogInformation(
                "The OBS watchdog does not watch OBS ({State}): {Reason}",
                state,
                State.Reason
            );
        }
    }

    private static string StreamName(ObsStreamHealth health) =>
        health == null ? "unknown" : health.State.ToString().ToLowerInvariant();

    private static string Describe(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalSeconds < 60)
        {
            return $"{(int)span.TotalSeconds}s";
        }

        return span.Seconds == 0
            ? $"{(int)span.TotalMinutes}m"
            : $"{(int)span.TotalMinutes}m {span.Seconds}s";
    }
}
