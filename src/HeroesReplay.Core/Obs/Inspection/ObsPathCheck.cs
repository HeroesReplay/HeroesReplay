using System;
using System.Collections.Generic;
using System.IO;

namespace HeroesReplay.Core.Obs.Inspection;

/// <summary>What <see cref="ObsPathCheck"/> found at a local path.</summary>
public enum ObsPathState
{
    /// <summary>The file or directory is there, at the path or under its link's target.</summary>
    Exists,

    /// <summary>The file system says nothing is there, at the path and under its link's target.</summary>
    Missing,

    /// <summary>
    /// This session could not tell: a junction or symbolic link on the way could not be read, or
    /// the file system would not say (for example a junction an SSH logon may not traverse).
    /// </summary>
    Unverifiable,
}

/// <summary>
/// One local path, checked through the junctions and symbolic links on the way (#335). A network
/// logon such as SSH may not traverse a junction that the desktop session follows (on the stream
/// PC <c>C:\heroesreplay</c> is a junction to <c>C:\SaltySadism</c>), so a path that is not found
/// is checked again under the first link's target, read without following the link.
/// <see cref="Resolved"/> is the path checked last. <see cref="Link"/> and <see cref="Target"/>
/// name that link, and <see cref="Reason"/> says why an <see cref="ObsPathState.Unverifiable"/>
/// path could not be checked.
/// </summary>
public sealed record ObsPathCheck(
    ObsPathState State,
    string Resolved,
    string Link = null,
    string Target = null,
    string Reason = null
)
{
    /// <summary>Links followed before a path is given up on, so a loop of links ends.</summary>
    public const int MaxLinks = 8;

    private static readonly char[] Separators =
    [
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar,
    ];

    /// <summary>Checks the fully qualified <paramref name="path"/> on <paramref name="files"/>.</summary>
    public static ObsPathCheck Of(string path, IObsFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return Of(path, files, 0);
    }

    private static ObsPathCheck Of(string path, IObsFileSystem files, int links)
    {
        string unreadable = null;
        try
        {
            if (files.Exists(path))
            {
                return new ObsPathCheck(ObsPathState.Exists, path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            unreadable = e.Message;
        }

        // The first link on the way decides: the same file is looked for under its target.
        foreach (string step in Steps(path))
        {
            string target;
            try
            {
                target = files.LinkTarget(step);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new ObsPathCheck(ObsPathState.Unverifiable, path, step, null, e.Message);
            }

            if (string.IsNullOrEmpty(target))
            {
                continue;
            }

            if (links >= MaxLinks)
            {
                return new ObsPathCheck(
                    ObsPathState.Unverifiable,
                    path,
                    step,
                    target,
                    "More than " + MaxLinks + " links on the way."
                );
            }

            string rest = path.Substring(step.Length).TrimStart(Separators);
            ObsPathCheck under = Of(
                rest.Length == 0 ? target : Path.Combine(target, rest),
                files,
                links + 1
            );

            // A link under the target that could not be read is the one to name.
            return under.State == ObsPathState.Unverifiable && under.Link != null
                ? under
                : under with
                {
                    Link = step,
                    Target = target,
                };
        }

        return unreadable == null
            ? new ObsPathCheck(ObsPathState.Missing, path)
            : new ObsPathCheck(ObsPathState.Unverifiable, path, Reason: unreadable);
    }

    /// <summary>
    /// Each folder on the way to <paramref name="path"/> below its root, then the path itself:
    /// <c>C:\a</c>, <c>C:\a\b</c>, <c>C:\a\b\c.png</c>.
    /// </summary>
    private static IEnumerable<string> Steps(string path)
    {
        string root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            yield break;
        }

        int start = root.Length;
        while (start < path.Length)
        {
            int end = path.IndexOfAny(Separators, start);
            if (end < 0)
            {
                yield return path;
                yield break;
            }

            if (end > start)
            {
                yield return path.Substring(0, end);
            }

            start = end + 1;
        }
    }
}
