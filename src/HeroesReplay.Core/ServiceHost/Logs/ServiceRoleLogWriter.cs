using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace HeroesReplay.Core.ServiceHost.Logs;

/// <summary>
/// Appends lines to one role's log file. A new file starts at local midnight and when the day's
/// file reaches the size limit; old files are pruned each time a file opens. A restarted role
/// appends to the day's file under a new header line. A write that fails is dropped, whatever
/// the exception, and the next write opens a file again, so logging never stops the role. That
/// next file is the day's next part, not the file that failed, and its first line after the
/// header says what failed (#479: on the stream PC a role's new day file stayed empty all day).
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
    private string failedPath;
    private string failure;
    private DateTimeOffset failedAt;
    private int dropped;

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

            string attempted = CurrentPath;
            try
            {
                DateOnly day = DateOnly.FromDateTime(at.DateTime);
                if (stream == null || day != openDay || length >= maxBytes)
                {
                    attempted = null;
                    Open(day, at, path => attempted = path);
                }

                Append(entry);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Fail(attempted, at, e);
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

    private void Open(DateOnly day, DateTimeOffset at, Action<string> opening)
    {
        Close();
        Directory.CreateDirectory(directory);

        // The first part of the day that still has room. A restarted role appends to it. A file
        // this writer could not write is skipped, whatever holds it.
        string path = Path.Combine(directory, ServiceRoleLog.FileName(role, day));
        for (int part = 1; part < 10000 && (Size(path) >= maxBytes || Failed(path)); part++)
        {
            path = Path.Combine(directory, ServiceRoleLog.FileName(role, day, part));
        }

        opening(path);
        stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete
        );
        length = stream.Length;
        openDay = day;
        CurrentPath = path;
        string stamp = at.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(header))
        {
            // Stamped like the entry that opened the file, so the header never sorts after it.
            Append(stamp + " --- " + header + " ---");
        }

        if (failure != null)
        {
            Append(
                $"{stamp} --- {dropped} log entries since "
                    + failedAt.ToString(TimestampFormat, CultureInfo.InvariantCulture)
                    + $" were lost: could not write {Path.GetFileName(failedPath ?? path)}. {failure} ---"
            );
            failure = null;
            dropped = 0;
        }

        // After the header, so a prune that fails or is slow never costs the file its first line.
        try
        {
            ServiceRoleLog.Prune(directory, role, day, retainedDays, maxFiles, path);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { }
    }

    private void Fail(string path, DateTimeOffset at, Exception e)
    {
        Close();
        dropped++;
        if (failure == null)
        {
            failedAt = at;
            failure = $"{e.GetType().Name}: {e.Message}".Replace(Environment.NewLine, " ");
        }

        if (path != null)
        {
            failedPath = path;
        }
    }

    private bool Failed(string path) =>
        failedPath != null && string.Equals(path, failedPath, StringComparison.OrdinalIgnoreCase);

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
        catch (Exception e) when (e is not OutOfMemoryException) { }

        stream = null;
    }

    private static long Size(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? file.Length : 0;
    }
}
