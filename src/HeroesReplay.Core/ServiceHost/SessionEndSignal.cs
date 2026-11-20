using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// Windows is ending this logon session: a restart, a shutdown, or a logoff, whether a person,
/// <c>services shutdown --restart</c> or Windows Update asked for it. The processes here load
/// user32, so Windows never sends them <c>CTRL_SHUTDOWN_EVENT</c>; it sends
/// <c>WM_QUERYENDSESSION</c> to a window instead. <see cref="Watch"/> subscribes to
/// <see cref="SystemEvents.SessionEnding"/> from a thread-pool (MTA) thread, so SystemEvents
/// runs its own hidden window and message loop. The supervisor reads <see cref="Ending"/> on
/// every pass and restarts nothing while Windows takes the processes down, instead of starting
/// roles and OBS again into a shutdown. When the process is still alive
/// <see cref="CancelledAfter"/> later, the shutdown was cancelled and supervision goes on.
/// </summary>
public sealed class SessionEndSignal
{
    /// <summary>A shutdown takes seconds. One that has not ended the process by then was cancelled.</summary>
    public static readonly TimeSpan CancelledAfter = TimeSpan.FromMinutes(3);

    /// <summary>The process-wide signal SystemEvents feeds once <see cref="Watch"/> ran.</summary>
    public static SessionEndSignal Default { get; } = new();

    private readonly object gate = new();
    private DateTimeOffset? since;
    private string reason;
    private int watching;

    /// <summary>Records that Windows asked to end the session, and why (<c>Logoff</c> or <c>SystemShutdown</c>).</summary>
    public void Mark(string why, DateTimeOffset at)
    {
        lock (gate)
        {
            since = at;
            reason = string.IsNullOrWhiteSpace(why) ? "SystemShutdown" : why;
        }
    }

    /// <summary>
    /// Why the session is ending, such as <c>SystemShutdown since 03:30:02Z</c>, or null when it
    /// is not, or the request is older than <see cref="CancelledAfter"/> (cancelled).
    /// </summary>
    public string Ending(DateTimeOffset now)
    {
        lock (gate)
        {
            if (since is not DateTimeOffset at || now - at > CancelledAfter)
            {
                return null;
            }

            return $"{reason} since {at:HH:mm:ss}Z";
        }
    }

    /// <summary>
    /// Starts listening for <see cref="SystemEvents.SessionEnding"/> once. Never throws: a process
    /// without a desktop (session 0, SSH) just never hears it.
    /// </summary>
    public void Watch(TimeProvider time = null)
    {
        if (Interlocked.Exchange(ref watching, 1) == 1)
        {
            return;
        }

        TimeProvider clock = time ?? TimeProvider.System;
        // From the thread pool (MTA): SystemEvents then makes its own window thread. On an STA
        // thread without a message loop it would make the window there and never pump it.
        Task.Run(() =>
        {
            try
            {
                SystemEvents.SessionEnding += (_, e) =>
                    Mark(e.Reason.ToString(), clock.GetUtcNow());
            }
            catch (Exception)
            {
                // No window station: nothing to hear.
            }
        });
    }
}
