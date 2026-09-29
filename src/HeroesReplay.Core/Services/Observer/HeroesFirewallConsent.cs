using System;
using System.Collections.Generic;
using System.IO;

namespace HeroesReplay.Core.Services.Observer;

public readonly record struct HeroesFirewallRule(string Name, string ProgramPath);

/// <summary>
/// Windows shows a consent dialog the first time each Heroes exe listens.
/// An inbound allow rule for that exe path keeps the dialog off the desktop.
/// </summary>
public static class HeroesFirewallConsent
{
    public static bool TryForClient(string exePath, out HeroesFirewallRule rule)
    {
        rule = default;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        if (
            !string.Equals(
                Path.GetFileName(exePath),
                InstalledClientCatalog.ExeFileName,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return false;
        }

        string folder = Path.GetFileName(Path.GetDirectoryName(exePath));
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        rule = new HeroesFirewallRule("HeroesReplay inbound " + folder, Path.GetFullPath(exePath));
        return true;
    }

    public static IReadOnlyList<HeroesFirewallRule> ForClients(IEnumerable<string> exePaths)
    {
        var rules = new List<HeroesFirewallRule>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (exePaths == null)
        {
            return rules;
        }

        foreach (string exePath in exePaths)
        {
            if (!TryForClient(exePath, out HeroesFirewallRule rule))
            {
                continue;
            }

            if (!seen.Add(rule.ProgramPath))
            {
                continue;
            }

            rules.Add(rule);
        }

        return rules;
    }
}
