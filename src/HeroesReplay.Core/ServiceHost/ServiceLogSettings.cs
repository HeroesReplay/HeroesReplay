using System;
using System.IO;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// Where each role keeps its own log file and how long the files stay. Bound from the
/// <c>ServiceLogs</c> section. The files do not depend on the Aspire dashboard.
/// </summary>
public sealed class ServiceLogSettings
{
    public const int DefaultMaxFileSizeMegabytes = 20;
    public const int DefaultRetainedDays = 14;
    public const int DefaultMaxFilesPerRole = 50;

    public bool Enabled { get; set; } = true;

    /// <summary>Empty means <c>%LOCALAPPDATA%\HeroesReplay\logs</c>.</summary>
    public string Directory { get; set; }

    /// <summary>A day's file rolls to <c>&lt;role&gt;-&lt;date&gt;.1.log</c> at this size.</summary>
    public int MaxFileSizeMegabytes { get; set; } = DefaultMaxFileSizeMegabytes;

    /// <summary>Files from more than this many days ago (today counts as one) are deleted.</summary>
    public int RetainedDays { get; set; } = DefaultRetainedDays;

    /// <summary>The newest files a role keeps, whatever their dates.</summary>
    public int MaxFilesPerRole { get; set; } = DefaultMaxFilesPerRole;

    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "logs"
        );

    public string ResolvedDirectory =>
        string.IsNullOrWhiteSpace(Directory)
            ? DefaultDirectory
            : Environment.ExpandEnvironmentVariables(Directory);

    public long MaxFileBytes =>
        (MaxFileSizeMegabytes > 0 ? MaxFileSizeMegabytes : DefaultMaxFileSizeMegabytes)
        * 1024L
        * 1024L;

    public int Days => RetainedDays > 0 ? RetainedDays : DefaultRetainedDays;

    public int MaxFiles => MaxFilesPerRole > 0 ? MaxFilesPerRole : DefaultMaxFilesPerRole;
}
