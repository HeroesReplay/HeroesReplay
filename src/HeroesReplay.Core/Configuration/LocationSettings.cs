namespace HeroesReplay.Core.Configuration;

public class LocationSettings
{
    public string BattlenetPath { get; set; }
    public string ReplaySource { get; set; }
    public string DataDirectory { get; set; }
    public string GameInstallDirectory { get; set; }

    /// <summary>
    /// Copies of HeroesOfTheStorm_x64.exe for patch iterations Battle.net may delete.
    /// Empty uses DataDirectory\Clients.
    /// </summary>
    public string RetainedClientDirectory { get; set; }
}
