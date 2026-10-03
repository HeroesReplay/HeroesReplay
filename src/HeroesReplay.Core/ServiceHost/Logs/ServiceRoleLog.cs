using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HeroesReplay.Core.ServiceHost.Logs;

/// <summary>One role log file: <c>&lt;role&gt;-&lt;yyyy-MM-dd&gt;[.&lt;part&gt;].log</c>.</summary>
public sealed record ServiceRoleLogFile(string Path, DateOnly Day, int Part);

/// <summary>
/// Names, finds, and prunes the per-role log files under <see cref="ServiceLogSettings"/>.
/// Only files that match a role's name pattern are touched; pid files and the Aspire dashboard
/// log in the same folder are left alone.
/// </summary>
public static class ServiceRoleLog
{
    /// <summary>The role name the supervisor logs under.</summary>
    public const string SupervisorRole = "supervisor";

    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>The role <c>services start</c> launched this process as, or null.</summary>
    public static string RoleFromEnvironment()
    {
        string role = Environment.GetEnvironmentVariable(ServiceReadyFile.RoleVariable);
        return IsSafeRole(role) ? role.ToLowerInvariant() : null;
    }

    public static bool IsSafeRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role) || role.Length > 32)
        {
            return false;
        }

        return role.All(character => char.IsLetterOrDigit(character) || character == '-');
    }

    public static string FileName(string role, DateOnly day, int part = 0) =>
        part <= 0
            ? $"{role}-{day.ToString(DayFormat, CultureInfo.InvariantCulture)}.log"
            : $"{role}-{day.ToString(DayFormat, CultureInfo.InvariantCulture)}.{part}.log";

    /// <summary>The role's files in <paramref name="directory"/>, newest first.</summary>
    public static IReadOnlyList<ServiceRoleLogFile> Files(string directory, string role)
    {
        if (!IsSafeRole(role) || string.IsNullOrWhiteSpace(directory))
        {
            return Array.Empty<ServiceRoleLogFile>();
        }

        string[] paths;
        try
        {
            if (!Directory.Exists(directory))
            {
                return Array.Empty<ServiceRoleLogFile>();
            }

            paths = Directory.GetFiles(directory, role + "-*.log");
        }
        catch (IOException)
        {
            return Array.Empty<ServiceRoleLogFile>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<ServiceRoleLogFile>();
        }

        var pattern = new Regex(
            "^" + Regex.Escape(role) + @"-(\d{4}-\d{2}-\d{2})(?:\.(\d{1,5}))?\.log$",
            RegexOptions.IgnoreCase
        );
        var files = new List<ServiceRoleLogFile>();
        foreach (string path in paths)
        {
            Match match = pattern.Match(Path.GetFileName(path));
            if (
                !match.Success
                || !DateOnly.TryParseExact(
                    match.Groups[1].Value,
                    DayFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly day
                )
            )
            {
                continue;
            }

            int part = match.Groups[2].Success
                ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
                : 0;
            files.Add(new ServiceRoleLogFile(path, day, part));
        }

        return files
            .OrderByDescending(file => file.Day)
            .ThenByDescending(file => file.Part)
            .ToList();
    }

    /// <summary>
    /// The file the role wrote last, or the file it writes today when there is none yet. This is
    /// the path <c>services status</c> shows.
    /// </summary>
    public static string LatestPath(string directory, string role, DateOnly today) =>
        Files(directory, role).FirstOrDefault()?.Path
        ?? Path.Combine(directory, FileName(role, today));

    /// <summary>
    /// Deletes the role's files older than <paramref name="retainedDays"/> days, then all but the
    /// newest <paramref name="maxFiles"/>. Never deletes <paramref name="keep"/>. Returns what went.
    /// </summary>
    public static IReadOnlyList<string> Prune(
        string directory,
        string role,
        DateOnly today,
        int retainedDays,
        int maxFiles,
        string keep = null
    )
    {
        DateOnly oldest = today.AddDays(-(Math.Max(1, retainedDays) - 1));
        var deleted = new List<string>();
        int kept = 0;
        foreach (ServiceRoleLogFile file in Files(directory, role))
        {
            bool current =
                keep != null && string.Equals(file.Path, keep, StringComparison.OrdinalIgnoreCase);
            if (current || (file.Day >= oldest && kept < Math.Max(1, maxFiles)))
            {
                kept++;
                continue;
            }

            try
            {
                File.Delete(file.Path);
                deleted.Add(file.Path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return deleted;
    }
}
