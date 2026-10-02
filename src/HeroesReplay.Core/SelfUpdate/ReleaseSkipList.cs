using System;
using System.Collections.Generic;
using System.IO;

namespace HeroesReplay.Core.SelfUpdate;

/// <summary>
/// Release tags this machine rolled back: <c>%LOCALAPPDATA%\HeroesReplay\updates\skipped-releases.txt</c>.
/// <c>apply-release.ps1</c> appends a line when a release fails its health gate, and
/// <see cref="IReleaseUpdateGate"/> never stages a listed tag again. A line is the tag, then
/// optional tab-separated notes (when, why). A line starting with <c>#</c> is a comment. Delete
/// the line to allow that release again.
/// </summary>
public sealed class ReleaseSkipList
{
    public const string FileName = "skipped-releases.txt";

    private readonly HashSet<string> tags;

    private ReleaseSkipList(HashSet<string> tags)
    {
        this.tags = tags;
    }

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "updates",
            FileName
        );

    public IReadOnlyCollection<string> Tags => tags;

    public bool Contains(string tag) =>
        !string.IsNullOrWhiteSpace(tag) && tags.Contains(tag.Trim());

    public static ReleaseSkipList Parse(string text)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (
            string raw in (text ?? string.Empty).Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string tag = line.Split(new[] { '\t', ' ' }, 2, StringSplitOptions.RemoveEmptyEntries)[
                0
            ];
            tags.Add(tag);
        }

        return new ReleaseSkipList(tags);
    }

    /// <summary>An unreadable or missing file skips nothing.</summary>
    public static ReleaseSkipList Load(string path = null)
    {
        path ??= DefaultPath;
        try
        {
            return Parse(File.Exists(path) ? File.ReadAllText(path) : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Parse(null);
        }
    }
}
