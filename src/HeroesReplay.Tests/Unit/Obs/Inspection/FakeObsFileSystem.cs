using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs.Inspection;

namespace HeroesReplay.Tests.Unit.Obs.Inspection;

/// <summary>
/// Named files and links for <see cref="ObsPathCheck"/>. A link in <see cref="Untraversable"/>
/// refuses every path below it, as a junction does under an SSH network logon on the stream PC;
/// one in <see cref="Unreadable"/> also refuses to say where it points.
/// </summary>
internal sealed class FakeObsFileSystem : IObsFileSystem
{
    public const string UntrustedMountPoint =
        "The path cannot be traversed because it contains an untrusted mount point.";

    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Links { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Untraversable { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The stream PC seen over SSH: <c>C:\heroesreplay</c> is a junction to <c>C:\SaltySadism</c>
    /// that this logon may not traverse.
    /// </summary>
    public static FakeObsFileSystem OverSsh(bool targetReadable = true)
    {
        var files = new FakeObsFileSystem();
        files.Links[@"C:\heroesreplay"] = @"C:\SaltySadism";
        files.Untraversable.Add(@"C:\heroesreplay");
        if (!targetReadable)
        {
            files.Unreadable.Add(@"C:\heroesreplay");
        }

        return files;
    }

    public FakeObsFileSystem With(params string[] paths)
    {
        Files.UnionWith(paths);
        return this;
    }

    public bool Exists(string path)
    {
        foreach ((string link, string target) in Links)
        {
            if (!IsBelow(path, link))
            {
                continue;
            }

            if (Untraversable.Contains(link))
            {
                throw new IOException(UntrustedMountPoint + " : '" + path + "'");
            }

            return Exists(target + path.Substring(link.Length));
        }

        return Links.ContainsKey(path)
            || Files.Contains(path)
            || Files.Any(file => IsBelow(file, path));
    }

    public string LinkTarget(string path)
    {
        if (!Links.TryGetValue(path, out string target))
        {
            return null;
        }

        if (Unreadable.Contains(path))
        {
            throw new UnauthorizedAccessException("Access to the path '" + path + "' is denied.");
        }

        return target;
    }

    private static bool IsBelow(string path, string folder) =>
        path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
}
