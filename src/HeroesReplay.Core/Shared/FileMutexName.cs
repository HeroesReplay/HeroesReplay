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

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant()));
        return sharedName + "." + Convert.ToHexString(hash, 0, 8);
    }
}
