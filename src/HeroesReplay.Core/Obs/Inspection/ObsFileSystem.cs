using System;
using System.IO;

namespace HeroesReplay.Core.Obs.Inspection;

/// <summary>
/// The file system reads behind the OBS file checks (<see cref="ObsPathCheck"/>), so a test can
/// stand in for a junction this logon cannot traverse (#335).
/// </summary>
public interface IObsFileSystem
{
    /// <summary>
    /// True when <paramref name="path"/> is a file or directory, false when the file system says
    /// nothing is there. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>
    /// when it cannot say, for example when the path goes through a junction this logon may not
    /// traverse ("untrusted mount point" under an SSH network logon).
    /// </summary>
    bool Exists(string path);

    /// <summary>
    /// Where the junction or symbolic link at <paramref name="path"/> itself points, as a full
    /// path, read without following it. Null when <paramref name="path"/> is not a link or is not
    /// there. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it
    /// is a link whose target cannot be read.
    /// </summary>
    string LinkTarget(string path);
}

/// <summary>The machine's own file system.</summary>
public sealed class ObsFileSystem : IObsFileSystem
{
    public static readonly ObsFileSystem Instance = new();

    private ObsFileSystem() { }

    public bool Exists(string path) => Attributes(path) != null;

    public string LinkTarget(string path)
    {
        FileAttributes? attributes = Attributes(path);
        if (attributes == null || !attributes.Value.HasFlag(FileAttributes.ReparsePoint))
        {
            return null;
        }

        FileSystemInfo link = attributes.Value.HasFlag(FileAttributes.Directory)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        string target = link.LinkTarget;
        if (string.IsNullOrEmpty(target))
        {
            // A reparse point that is not a link: a cloud placeholder, a deduplicated file.
            return null;
        }

        // A volume mount point names its volume without the \\?\ prefix.
        if (target.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\?\" + target;
        }

        // A symbolic link's target can be relative to the folder the link is in.
        string folder = Path.GetDirectoryName(Path.GetFullPath(path));
        return folder == null ? Path.GetFullPath(target) : Path.GetFullPath(target, folder);
    }

    /// <summary>
    /// The attributes of <paramref name="path"/> itself (a link is not followed), or null when
    /// nothing is there. Every other failure is thrown, unlike <see cref="File.Exists"/>, which
    /// reads a junction it may not traverse as a missing file.
    /// </summary>
    private static FileAttributes? Attributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception e)
            when (e
                    is FileNotFoundException
                        or DirectoryNotFoundException
                        or DriveNotFoundException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return null;
        }
    }
}
