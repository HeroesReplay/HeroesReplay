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
}
