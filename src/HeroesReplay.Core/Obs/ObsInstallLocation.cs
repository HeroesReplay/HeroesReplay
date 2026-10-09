using Microsoft.Win32;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// Where the OBS Studio installer put OBS: the default value of <c>HKLM\SOFTWARE\OBS Studio</c>,
/// for example <c>C:\Program Files\obs-studio</c> (#398). Read-only.
/// </summary>
public static class ObsInstallLocation
{
    public const string KeyPath = @"SOFTWARE\OBS Studio";

    /// <summary>The install folder, or null when OBS was not installed by its installer.</summary>
    public static string FromRegistry()
    {
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey key = machine.OpenSubKey(KeyPath);
            if (key?.GetValue(string.Empty) is string folder && !string.IsNullOrWhiteSpace(folder))
            {
                return folder;
            }
        }

        return null;
    }
}
