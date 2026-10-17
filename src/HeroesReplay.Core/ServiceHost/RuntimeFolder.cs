using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// The .NET shared runtime folder this process loaded, and whether it is still there (#450).
/// Windows Update services .NET under running processes: on 2026-10-15 it installed 10.0.13 and
/// deleted the 10.0.12 folder that the stream PC's supervisor and four roles had loaded two days
/// before. Every runtime library a process loads after that fails (the release unzip's zlib
/// first), so a process whose folder is gone restarts onto the current runtime
/// (<see cref="ServiceHealthCodes.RuntimeServiced"/>).
/// </summary>
/// <remarks>
/// Only what was there when this object was made counts: the folder, and the
/// <see cref="Markers"/> it held. A folder that was already missing then (a proof's fake folder
/// after its deletion, a self-contained build's own folder without the markers) never reports.
/// Once gone, it stays gone for this process.
/// </remarks>
public sealed class RuntimeFolder
{
    /// <summary>Files of the shared framework that a serviced folder loses with it.</summary>
    public static readonly IReadOnlyList<string> Markers = new[]
    {
        "System.Private.CoreLib.dll",
        "System.IO.Compression.Native.dll",
        "Microsoft.NETCore.App.deps.json",
    };

    private readonly Func<string, bool> directoryExists;
    private readonly Func<string, bool> fileExists;
    private readonly bool watched;
    private readonly List<string> markers;
    private volatile string missing;

    public RuntimeFolder(
        string folder,
        Func<string, bool> directoryExists = null,
        Func<string, bool> fileExists = null
    )
    {
        Folder = string.IsNullOrWhiteSpace(folder)
            ? null
            : folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        this.directoryExists = directoryExists ?? Directory.Exists;
        this.fileExists = fileExists ?? File.Exists;
        watched = Folder != null && this.directoryExists(Folder);
        markers = watched
            ? Markers.Select(name => Path.Combine(Folder, name)).Where(this.fileExists).ToList()
            : new List<string>();
    }

    /// <summary>
    /// The folder this process's runtime loaded from. <paramref name="overrideFolder"/>
    /// (<see cref="ServiceHealthSettings.RuntimeDirectory"/>) watches another folder instead, for
    /// a proof.
    /// </summary>
    public static RuntimeFolder ForThisProcess(string overrideFolder = null) =>
        new(
            string.IsNullOrWhiteSpace(overrideFolder)
                ? RuntimeEnvironment.GetRuntimeDirectory()
                : overrideFolder
        );

    /// <summary>The watched folder, without a trailing separator.</summary>
    public string Folder { get; }

    /// <summary>False when the folder did not exist when this was made, so it never reports.</summary>
    public bool Watched => watched;

    /// <summary>Null while the folder and its markers are there. Once one is gone, why.</summary>
    public string Missing()
    {
        if (missing != null || !watched)
        {
            return missing;
        }

        if (!directoryExists(Folder))
        {
            missing =
                $"The .NET runtime folder {Folder} that this process loaded is gone: a .NET update serviced it.";
        }
        else if (markers.FirstOrDefault(path => !fileExists(path)) is string lost)
        {
            missing =
                $"{lost}, part of the .NET runtime this process loaded, is gone: a .NET update is servicing it.";
        }

        return missing;
    }
}
