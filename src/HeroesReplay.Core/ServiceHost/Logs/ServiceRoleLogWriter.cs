using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace HeroesReplay.Core.ServiceHost.Logs;

/// <summary>
/// Appends lines to one role's log file. A new file starts at local midnight and when the day's
/// file reaches the size limit; old files are pruned each time a file opens. A restarted role
/// appends to the day's file under a new header line. A write that fails is dropped, and the
/// next write opens the file again, so logging never stops the role.
/// </summary>
public sealed class ServiceRoleLogWriter : IDisposable
{
    public const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz";

    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    private readonly object gate = new();
    private readonly string role;
    private readonly string directory;
    private readonly long maxBytes;
    private readonly int retainedDays;
    private readonly int maxFiles;
    private readonly TimeProvider time;
    private readonly string header;
    private FileStream stream;
    private DateOnly openDay;
    private long length;
    private bool disposed;

    public ServiceRoleLogWriter(
        string role,
        string directory,
        long maxBytes,
        int retainedDays,
        int maxFiles,
        TimeProvider time = null,
        string header = null
    )
    {
        if (!ServiceRoleLog.IsSafeRole(role))
        {
            throw new ArgumentException($"`{role}` is not a role name.", nameof(role));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.role = role;
        this.directory = directory;
        this.maxBytes = maxBytes > 0 ? maxBytes : 1;
        this.retainedDays = retainedDays;
        this.maxFiles = maxFiles;
        this.time = time ?? TimeProvider.System;
        this.header = header;
    }

    /// <summary>The file the last write went to. Null until the first write.</summary>
    public string CurrentPath { get; private set; }

    /// <summary>The local time a line written now is stamped with.</summary>
    public DateTimeOffset LocalNow() =>
        TimeZoneInfo.ConvertTime(time.GetUtcNow(), time.LocalTimeZone);

    /// <summary>Writes one entry stamped now.</summary>
    public void Write(string entry) => Write(LocalNow(), entry);

    /// <summary>
    /// Writes one entry that was stamped <paramref name="at"/> (local time), into that day's
    /// file. Continuation lines should already be indented.
    /// </summary>
    public void Write(DateTimeOffset at, string entry)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            try
            {
                DateOnly day = DateOnly.FromDateTime(at.DateTime);
                if (stream == null || day != openDay || length >= maxBytes)
                {
                    Open(day, at);
                }

                Append(entry);
            }
            catch (IOException)
            {
                Close();
            }
            catch (UnauthorizedAccessException)
            {
                Close();
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            Close();
        }
    }

    private void Open(DateOnly day, DateTimeOffset at)
    {
        Close();
        Directory.CreateDirectory(directory);

        // The first part of the day that still has room. A restarted role appends to it.
        string path = Path.Combine(directory, ServiceRoleLog.FileName(role, day));
        for (int part = 1; part < 10000 && Size(path) >= maxBytes; part++)
        {
            path = Path.Combine(directory, ServiceRoleLog.FileName(role, day, part));
        }

        stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete
        );
        length = stream.Length;
        openDay = day;
        CurrentPath = path;
        ServiceRoleLog.Prune(directory, role, day, retainedDays, maxFiles, path);
        if (!string.IsNullOrWhiteSpace(header))
        {
            // Stamped like the entry that opened the file, so the header never sorts after it.
            Append(
                at.ToString(TimestampFormat, CultureInfo.InvariantCulture)
                    + " --- "
                    + header
                    + " ---"
            );
        }
    }

    private void Append(string entry)
    {
        byte[] bytes = Utf8.GetBytes(entry + Environment.NewLine);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
        length += bytes.Length;
    }

    private void Close()
    {
        try
        {
            stream?.Dispose();
        }
        catch (IOException) { }

        stream = null;
    }

    private static long Size(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? file.Length : 0;
    }
}
