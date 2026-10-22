using System;

namespace HeroesReplay.Core.SelfUpdate;

public class ReleaseSettings
{
    public const string DefaultRepository = "HeroesReplay/HeroesReplay";
    public const string DefaultAssetName = "heroesreplay-win-x64.zip";
    public const string VersionFileName = "version.txt";
    public const string SecretsFileName = "appsettings.secrets.json";
    public const string DefaultSecretsPath = @"C:\heroesreplay\secrets\appsettings.secrets.json";

    public bool Enabled { get; set; }

    public string Repository { get; set; } = DefaultRepository;

    public string AssetName { get; set; } = DefaultAssetName;

    public string SecretsPath { get; set; } = DefaultSecretsPath;

    /// <summary>
    /// How long <c>apply-release.ps1</c> gives a new install to be healthy
    /// (<see cref="ReleaseHealth"/>) before it restores <c>.previous</c>.
    /// </summary>
    public TimeSpan HealthWindow { get; set; } = ReleaseHealth.DefaultWindow;

    public const int DefaultRetainedVersions = 1;
    public const int DefaultRetainedLogs = 10;

    /// <summary>
    /// How many releases older than the installed one the housekeeping pass keeps in
    /// <c>%LOCALAPPDATA%\HeroesReplay\updates</c>, besides the one <c>app.previous</c> holds (#488).
    /// </summary>
    public int RetainedVersions { get; set; } = DefaultRetainedVersions;

    /// <summary>
    /// How many earlier updates' logs (<c>apply-release.&lt;time&gt;.log</c>,
    /// <c>apply-release-&lt;tag&gt;.log</c>) the housekeeping pass keeps (#488).
    /// </summary>
    public int RetainedLogs { get; set; } = DefaultRetainedLogs;
}
