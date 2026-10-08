using System;
using System.IO;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// One <c>services ensure</c> at a time, across logon sessions: an exclusive handle on
/// <c>%LOCALAPPDATA%\HeroesReplay\services.ensure.lock</c>, held while the ensure decides and starts
/// roles (not while it supervises). Two ensures that both saw a role down would otherwise start it
/// twice. The file goes when the handle closes, and Windows closes it if the process dies.
/// </summary>
public sealed class ServiceEnsureLock : IDisposable
{
    private FileStream stream;

    private ServiceEnsureLock(FileStream stream)
    {
        this.stream = stream;
    }

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "services.ensure.lock"
        );

    /// <summary>The lock, or null while another ensure holds it.</summary>
    public static ServiceEnsureLock TryAcquire(string path = null)
    {
        string file = path ?? DefaultPath;
        string directory = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            return new ServiceEnsureLock(
                new FileStream(
                    file,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.DeleteOnClose
                )
            );
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        stream?.Dispose();
        stream = null;
    }
}
