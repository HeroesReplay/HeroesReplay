using System;
using System.IO;

namespace HeroesReplay.Core.GameClient;

public sealed record ReplayStartCommand(string FileName, string Arguments, string WorkingDirectory)
{
    public static ReplayStartCommand For(string gameInstallDirectory, string replayPath)
    {
        if (string.IsNullOrWhiteSpace(replayPath))
        {
            throw new ArgumentException("Replay path is required.", nameof(replayPath));
        }

        string switcher = string.IsNullOrWhiteSpace(gameInstallDirectory)
            ? null
            : Path.Combine(gameInstallDirectory, "Support64", "HeroesSwitcher_x64.exe");
        if (!string.IsNullOrWhiteSpace(switcher) && File.Exists(switcher))
        {
            return new ReplayStartCommand(
                switcher,
                Quote(replayPath),
                Path.GetDirectoryName(switcher)
            );
        }

        string comspec = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(comspec))
        {
            comspec = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        }

        return new ReplayStartCommand(
            comspec,
            "/c start \"\" " + Quote(replayPath),
            Path.GetDirectoryName(replayPath)
        );
    }

    public static ReplayStartCommand MatchingExe(string exePath, string replayPath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            throw new ArgumentException("Heroes exe is required.", nameof(exePath));
        }

        if (string.IsNullOrWhiteSpace(replayPath))
        {
            throw new ArgumentException("Replay path is required.", nameof(replayPath));
        }

        return new ReplayStartCommand(exePath, Quote(replayPath), Path.GetDirectoryName(exePath));
    }

    public static ReplayStartCommand HeroClient(string battleNetPath)
    {
        if (string.IsNullOrWhiteSpace(battleNetPath))
        {
            throw new ArgumentException("Battle.net path is required.", nameof(battleNetPath));
        }

        return new ReplayStartCommand(
            battleNetPath,
            "--exec=\"launch Hero\"",
            Path.GetDirectoryName(battleNetPath)
        );
    }

    public static string Quote(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "", StringComparison.Ordinal) + "\"";
    }
}
