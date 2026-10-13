using System;
using System.ComponentModel;
using System.Diagnostics;
using HeroesClientSDK;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.Core.Spectating.Control;

/// <summary>One client process: its pid and start time (UTC ticks, 0 when Windows hides it).</summary>
public readonly record struct ClientProcessKey(int Pid, long StartTicks)
{
    /// <summary>Null when <paramref name="process"/> is null, has exited, or cannot be read.</summary>
    public static ClientProcessKey? Of(Process process)
    {
        if (process == null)
        {
            return null;
        }

        try
        {
            if (process.HasExited)
            {
                return null;
            }

            long started;
            try
            {
                started = process.StartTime.ToUniversalTime().Ticks;
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException)
            {
                started = 0;
            }

            return new ClientProcessKey(process.Id, started);
        }
        catch (Exception e)
            when (e is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>
/// One HeroesClientSDK <see cref="HeroesClientProcess"/> per running client (pid and start time),
/// shared by every memory reader of the spectator (#382): the match clock (<c>StableGameTimer</c>
/// and the launch probe in <see cref="GameController"/>), <see cref="LoadingScreen"/>,
/// <see cref="ClientScreen"/>, and the running-build check. The client's read-only handle is
/// opened once, and the two screen readers walk its code once between them (HeroesClientSDK
/// 0.4.2). A new process (a HeroesSwitcher handoff, a relaunch) is attached again and the old
/// client is disposed as soon as no read still uses it. A read without a live process detaches.
/// </summary>
public sealed class SharedClientProcess : IDisposable
{
    private readonly object gate = new();
    private readonly ILogger logger;
    private readonly Func<Process, HeroesClientProcess> attach;
    private Attachment current;
    private string lastFailure;
    private int attaches;
    private int disposed;

    public SharedClientProcess(ILogger<SharedClientProcess> logger)
        : this(logger, HeroesClientProcess.Attach) { }

    internal SharedClientProcess(ILogger logger, Func<Process, HeroesClientProcess> attach)
    {
        this.logger = logger ?? NullLogger.Instance;
        this.attach = attach ?? throw new ArgumentNullException(nameof(attach));
    }

    /// <summary>How many clients were attached. A failed attach that is retried counts again.</summary>
    public int Attaches
    {
        get
        {
            lock (gate)
            {
                return attaches;
            }
        }
    }

    /// <summary>How many attached clients were disposed after a handoff, an exit, or a detach.</summary>
    public int Disposed
    {
        get
        {
            lock (gate)
            {
                return disposed;
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="read"/> on the client attached to <paramref name="process"/>: the same
    /// one while the pid and start time are the same, and while it stays attached. A client that
    /// could not attach (a client that has only just started has no module yet) is attached again
    /// on the next read. With no live process, the attachment is dropped and
    /// <paramref name="read"/> gets null, which every SDK reader answers with "no-process".
    /// </summary>
    public T Read<T>(Process process, Func<HeroesClientProcess, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (ClientProcessKey.Of(process) is not ClientProcessKey key)
        {
            Detach();
            return read(null);
        }

        return Read(key, () => attach(process), read);
    }

    /// <summary>The same as <see cref="Read{T}(Process, Func{HeroesClientProcess, T})"/>, by key.</summary>
    internal T Read<T>(
        ClientProcessKey key,
        Func<HeroesClientProcess> attachNew,
        Func<HeroesClientProcess, T> read
    )
    {
        Attachment attachment = Acquire(key, attachNew);
        try
        {
            return read(attachment.Client);
        }
        finally
        {
            Release(attachment);
        }
    }

    /// <summary>
    /// Drops the attached client: the game exited or was killed. It is disposed as soon as no read
    /// still uses it.
    /// </summary>
    public void Detach()
    {
        lock (gate)
        {
            Retire(current);
            current = null;
        }
    }

    public void Dispose() => Detach();

    private Attachment Acquire(ClientProcessKey key, Func<HeroesClientProcess> attachNew)
    {
        lock (gate)
        {
            if (current == null || current.Key != key || !current.Client.Ok)
            {
                bool handoff = current != null && current.Key != key;
                Retire(current);
                current = new Attachment(key, attachNew() ?? HeroesClientProcess.Attach(null));
                attaches++;
                LogAttach(current, handoff);
            }

            current.Reads++;
            return current;
        }
    }

    private void Release(Attachment attachment)
    {
        lock (gate)
        {
            attachment.Reads--;
            if (attachment.Retired && attachment.Reads == 0)
            {
                Close(attachment);
            }
        }
    }

    private void Retire(Attachment attachment)
    {
        if (attachment == null)
        {
            return;
        }

        attachment.Retired = true;
        if (attachment.Reads == 0)
        {
            Close(attachment);
        }
    }

    private void Close(Attachment attachment)
    {
        if (attachment.Closed)
        {
            return;
        }

        attachment.Closed = true;
        attachment.Client.Dispose();
        disposed++;
    }

    private void LogAttach(Attachment attachment, bool handoff)
    {
        HeroesClientProcess client = attachment.Client;
        if (!client.Ok)
        {
            string failure = $"{attachment.Key.Pid}:{attachment.Key.StartTicks}:{client.Reason}";
            if (failure != lastFailure)
            {
                lastFailure = failure;
                logger.LogDebug(
                    "Heroes pid {Pid} is not attached for the memory readers yet ({Reason}).",
                    attachment.Key.Pid,
                    client.Reason
                );
            }

            return;
        }

        lastFailure = null;
        logger.LogInformation(
            "Attached the memory readers to Heroes pid {Pid} (build {Build}){Handoff}. The match clock, both screen readers and the build check share this client.",
            attachment.Key.Pid,
            client.DetectedVersion?.ToString() ?? "unknown",
            handoff ? " after the client process changed" : string.Empty
        );
    }

    private sealed class Attachment(ClientProcessKey key, HeroesClientProcess client)
    {
        public ClientProcessKey Key { get; } = key;
        public HeroesClientProcess Client { get; } = client;
        public int Reads { get; set; }
        public bool Retired { get; set; }
        public bool Closed { get; set; }
    }
}
