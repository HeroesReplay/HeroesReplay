using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HeroesReplay.Core.Shared;

/// <summary>
/// The name of the session-wide mutex that guards one file shared between processes. The file
/// the roles use keeps the name every release has used, so an older and a newer build still
/// exclude each other while a release hands over. Any other file (a test's temp file) gets a
/// name of its own, from its full path, so it never waits on the running stack or on another
/// test run on the same machine (#331).
/// </summary>
public static class FileMutexName
{
    public static string For(string sharedName, string sharedPath, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedName);
        string full = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? sharedPath : path);
        if (string.Equals(full, Path.GetFullPath(sharedPath), StringComparison.OrdinalIgnoreCase))
        {
            return sharedName;
        }

        return Hashed(sharedName, full);
    }

    /// <summary>
    /// A name from the file's full path alone, for a file new enough that no older build locks
    /// it under a fixed name: every process that opens the same path agrees on it, and a test's
    /// temp file always has its own.
    /// </summary>
    public static string ForFile(string prefix, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Hashed(prefix, Path.GetFullPath(path));
    }

    private static string Hashed(string name, string fullPath)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant()));
        return name + "." + Convert.ToHexString(hash, 0, 8);
    }
}
