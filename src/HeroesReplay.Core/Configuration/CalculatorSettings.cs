using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration;

/// <summary>
/// Per-calculator on/off flags keyed by type name (e.g. KillCalculator).
/// Missing keys default to enabled so A/B tests can turn a subset off.
/// </summary>
public class CalculatorSettings
{
    public Dictionary<string, bool> Enabled { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled(Type calculatorType)
    {
        if (calculatorType == null)
        {
            return false;
        }

        return IsEnabled(calculatorType.Name);
    }

    public bool IsEnabled(string calculatorName)
    {
        if (string.IsNullOrWhiteSpace(calculatorName))
        {
            return false;
        }

        if (Enabled == null || Enabled.Count == 0)
        {
            return true;
        }

        return !Enabled.TryGetValue(calculatorName, out bool enabled) || enabled;
    }
}
