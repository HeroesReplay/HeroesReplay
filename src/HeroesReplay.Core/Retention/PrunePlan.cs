using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HeroesReplay.Core.Retention;

/// <summary>One file or folder a pruner would remove, and why.</summary>
public sealed record PruneItem(string Path, long Bytes, DateTimeOffset LastWriteUtc, string Reason)
{
    public bool IsDirectory { get; init; }

    /// <summary>
    /// Logged as a warning when the item is removed, for a removal an operator should hear about
    /// (a recording that was never uploaded). Null for routine removals.
    /// </summary>
    public string Warning { get; init; }

    public static PruneItem ForFile(FileInfo file, string reason, string warning = null) =>
        new(file.FullName, file.Length, file.LastWriteTimeUtc, reason) { Warning = warning };

    public static PruneItem ForDirectory(DirectoryInfo directory, string reason) =>
        new(directory.FullName, SizeOf(directory), directory.LastWriteTimeUtc, reason)
        {
            IsDirectory = true,
        };

    /// <summary>Every file under <paramref name="directory"/>, in bytes. Zero when it cannot be read.</summary>
    public static long SizeOf(DirectoryInfo directory)
    {
        try
        {
            return directory.Exists
                ? directory
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(file => file.Length)
                : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}

/// <summary>
/// What one pruner would remove (#487). Items are applied in order, so a file is listed before
/// the folder that holds it when both go. <see cref="Bytes"/> and <see cref="Count"/> count an
/// item inside a listed folder once, with the folder.
/// </summary>
public sealed class PrunePlan
{
    public PrunePlan(string pruner)
    {
        Pruner = pruner;
    }

    public string Pruner { get; }

    public List<PruneItem> Items { get; } = new();

    /// <summary>What the pruner could not read while it planned. The pass logs them.</summary>
    public List<string> Warnings { get; } = new();

    public void Add(PruneItem item)
    {
        if (item != null)
        {
            Items.Add(item);
        }
    }

    /// <summary>The items that are not inside a folder this plan removes.</summary>
    public IReadOnlyList<PruneItem> TopLevel
    {
        get
        {
            List<string> folders = Items
                .Where(item => item.IsDirectory)
                .Select(item => Path.TrimEndingDirectorySeparator(item.Path))
                .ToList();
            return Items
                .Where(item =>
                    !folders.Any(folder =>
                        !string.Equals(item.Path, folder, StringComparison.OrdinalIgnoreCase)
                        && item.Path.StartsWith(
                            folder + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                .ToList();
        }
    }

    public int Count => TopLevel.Count;

    public long Bytes => TopLevel.Sum(item => item.Bytes);

    /// <summary>Removes every item in order. The default apply of a pruner.</summary>
    public RetentionSweep DeleteAll()
    {
        var result = new RetentionSweep();
        result.Warnings.AddRange(Warnings);
        foreach (PruneItem item in Items)
        {
            if (item.IsDirectory)
            {
                MediaRetention.DeleteDirectory(new DirectoryInfo(item.Path), result);
            }
            else
            {
                MediaRetention.DeleteFile(item.Path, result, item.Warning);
            }
        }

        return result;
    }
}
