using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HeroesReplay.Core.SelfUpdate;

/// <summary>
/// <c>%LOCALAPPDATA%\HeroesReplay\start-live.cmd</c>, the launcher a <c>HeroesReplay-live</c> task
/// runs when it was made before <c>services install-task</c>. A hand-made one may start the roles
/// in <c>cmd /k</c> windows, or only some of them, and the health gate then sees no stack. A
/// release rewrites it to <c>services start --supervise</c> with every role. The launcher it
/// replaced is kept as <see cref="BackupName"/> and put back on a rollback, because the install
/// rolled back to was started by it.
/// </summary>
public static partial class ReleaseLauncher
{
    public const string FileName = "start-live.cmd";
    public const string BackupName = "start-live.cmd.previous";

    private const string EnvironmentVariable = "HEROES_REPLAY_ENV";

    public static string DefaultStateDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay"
        );

    /// <summary>
    /// The launcher that starts <paramref name="installDirectory"/> supervised. Each
    /// <c>set HEROES_REPLAY_…</c> line of <paramref name="current"/> is kept, except the
    /// environment, which is <paramref name="environment"/>.
    /// </summary>
    public static string Rewrite(string current, string installDirectory, string environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        string install = installDirectory.Trim().TrimEnd('\\');
        var lines = new List<string>
        {
            "@echo off",
            "rem Written by apply-release.ps1 (heroesreplay update launcher). The launcher it replaced is "
                + BackupName
                + ".",
            "setlocal",
            "set " + EnvironmentVariable + "=" + environment.Trim(),
        };
        lines.AddRange(KeptSettings(current));
        lines.Add("cd /d \"" + install + "\"");
        lines.Add("\"" + install + "\\heroesreplay.exe\" services start --supervise");
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>Backs up the launcher and rewrites it. Returns one line for the update log.</summary>
    public static string Replace(string stateDirectory, string installDirectory, string environment)
    {
        string launcher = Path.Combine(stateDirectory, FileName);
        string backup = Path.Combine(stateDirectory, BackupName);
        if (!File.Exists(launcher))
        {
            // An older backup must not be put back if this release is rolled back.
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }

            return $"No {FileName}. The HeroesReplay-live task or services start starts the stack.";
        }

        string current = File.ReadAllText(launcher);
        File.Copy(launcher, backup, overwrite: true);
        string rewritten = Rewrite(current, installDirectory, environment);
        if (string.Equals(current, rewritten, StringComparison.Ordinal))
        {
            return $"{FileName} already starts every role supervised.";
        }

        string temp = launcher + ".tmp";
        File.WriteAllText(temp, rewritten, new UTF8Encoding(false));
        File.Move(temp, launcher, overwrite: true);
        return $"Rewrote {FileName} to start every role supervised (services start --supervise). The launcher it replaced is {BackupName}.";
    }

    /// <summary>Puts the replaced launcher back for the install a rollback restores.</summary>
    public static string Restore(string stateDirectory)
    {
        string backup = Path.Combine(stateDirectory, BackupName);
        if (!File.Exists(backup))
        {
            return $"No {BackupName}. {FileName} was left as it is.";
        }

        File.Copy(backup, Path.Combine(stateDirectory, FileName), overwrite: true);
        return $"Restored {FileName} from {BackupName} for the install rolled back to.";
    }

    private static IEnumerable<string> KeptSettings(string current)
    {
        // In file order. A name set twice keeps its last value, in its first place.
        var kept = new List<(string Name, string Line)>();
        foreach (string line in (current ?? string.Empty).Split('\n'))
        {
            Match setting = Setting().Match(line);
            string name = setting.Groups["name"].Value;
            if (
                !setting.Success
                || string.Equals(name, EnvironmentVariable, StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            int at = kept.FindIndex(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
            );
            if (at >= 0)
            {
                kept[at] = (name, line.Trim());
            }
            else
            {
                kept.Add((name, line.Trim()));
            }
        }

        return kept.Select(item => item.Line);
    }

    [GeneratedRegex(
        @"^\s*set\s+""?(?<name>HEROES_REPLAY_[A-Za-z0-9_]+)=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex Setting();
}
