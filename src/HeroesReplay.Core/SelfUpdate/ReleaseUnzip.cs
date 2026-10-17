using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.Core.SelfUpdate;

/// <summary>
/// Unzips a downloaded release, and tells a runtime library that did not load from any other
/// failure (#450). On the stream PC on 2026-10-17 the unzip threw
/// <c>ZLibException: The underlying compression routine could not be loaded correctly ---&gt;
/// DllNotFoundException</c> 24 times: a .NET update had deleted the runtime folder the process
/// loaded, zlib with it, and the gate logged "This version keeps running." Now that failure is
/// <see cref="ServiceHealthCodes.RuntimeServiced"/>, and the zip is unzipped again by
/// <c>tar.exe</c> in a process of its own, which no .NET runtime update can take a library from.
/// </summary>
public static class ReleaseUnzip
{
    /// <summary>The unzip's own exception when zlib does not load; internal to the runtime.</summary>
    public const string ZLibExceptionType = "System.IO.Compression.ZLibException";

    /// <summary>The child-process unzip never waits longer than this.</summary>
    public static readonly TimeSpan ChildTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Windows' own bsdtar, which unzips .zip files.</summary>
    public static string TarPath => Path.Combine(Environment.SystemDirectory, "tar.exe");

    /// <summary>
    /// True when <paramref name="error"/> or an exception inside it is a runtime library that did
    /// not load: a <see cref="DllNotFoundException"/>, or the unzip's ZLibException.
    /// </summary>
    public static bool IsRuntimeLoadFailure(Exception error)
    {
        for (Exception e = error; e != null; e = e.InnerException)
        {
            if (
                e is DllNotFoundException
                || string.Equals(e.GetType().FullName, ZLibExceptionType, StringComparison.Ordinal)
            )
            {
                return true;
            }

            if (e is AggregateException aggregate)
            {
                return aggregate.InnerExceptions.Any(IsRuntimeLoadFailure);
            }
        }

        return false;
    }

    /// <summary>The cause a heartbeat reports for a runtime library that did not load.</summary>
    public static string Cause(Exception error)
    {
        Exception root = error;
        while (root?.InnerException != null)
        {
            root = root.InnerException;
        }

        return root == null
            ? "A .NET runtime library did not load."
            : $"A .NET runtime library did not load ({root.GetType().Name}: {root.Message.Trim()}).";
    }

    /// <summary>
    /// When <paramref name="error"/> is a runtime library that did not load, hands its cause to
    /// <paramref name="serviced"/> (<see cref="ServiceHeartbeat.RecordRuntimeServiced"/>) and
    /// returns true: the process restarts onto the current runtime instead of keeping running.
    /// </summary>
    public static bool ReportServiced(Exception error, Action<string> serviced)
    {
        if (!IsRuntimeLoadFailure(error))
        {
            return false;
        }

        serviced?.Invoke(Cause(error));
        return true;
    }

    /// <summary>
    /// Unzips <paramref name="zip"/> into <paramref name="folder"/> in this process. When that
    /// cannot load a runtime library, <paramref name="runtimeFailure"/> hears why and a child
    /// process unzips it instead. True when the child process did.
    /// </summary>
    public static bool Extract(
        string zip,
        string folder,
        Action<Exception> runtimeFailure,
        Action<string, string> inProcess = null,
        Action<string, string> childProcess = null
    )
    {
        try
        {
            (inProcess ?? ZipFile.ExtractToDirectory).Invoke(zip, folder);
            return false;
        }
        catch (Exception e) when (IsRuntimeLoadFailure(e))
        {
            runtimeFailure?.Invoke(e);
        }

        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        (childProcess ?? ExtractWithTar).Invoke(zip, folder);
        return true;
    }

    /// <summary><c>tar.exe -xf &lt;zip&gt; -C &lt;folder&gt;</c>, with no window.</summary>
    public static ProcessStartInfo TarStart(string zip, string folder)
    {
        var start = new ProcessStartInfo
        {
            FileName = TarPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-xf");
        start.ArgumentList.Add(zip);
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(folder);
        return start;
    }

    private static void ExtractWithTar(string zip, string folder)
    {
        Directory.CreateDirectory(folder);
        using Process tar =
            Process.Start(TarStart(zip, folder))
            ?? throw new IOException($"{TarPath} did not start.");
        Task<string> error = tar.StandardError.ReadToEndAsync();
        if (!tar.WaitForExit(ChildTimeout))
        {
            try
            {
                tar.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }

            throw new IOException(
                $"{TarPath} did not unzip {zip} within {ChildTimeout.TotalMinutes:0} minutes."
            );
        }

        if (tar.ExitCode != 0)
        {
            throw new IOException(
                $"{TarPath} could not unzip {zip} (exit {tar.ExitCode}): {error.GetAwaiter().GetResult().Trim()}"
            );
        }
    }
}
