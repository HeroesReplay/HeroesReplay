using System;
using System.Collections.Generic;
using System.Text;

namespace HeroesReplay.Core.Services.Client;

public static class StormVariablesEditor
{
    public static Dictionary<string, string> Parse(string contents)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(contents))
        {
            return values;
        }

        foreach (string line in contents.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            values[line[..eq]] = line[(eq + 1)..];
        }

        return values;
    }

    public static string Apply(string contents, IReadOnlyDictionary<string, string> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);

        var remaining = new Dictionary<string, string>(updates, StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder();
        string[] lines = string.IsNullOrEmpty(contents)
            ? Array.Empty<string>()
            : contents.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        foreach (string line in lines)
        {
            int eq = line.IndexOf('=');
            if (eq > 0)
            {
                string key = line[..eq];
                if (remaining.TryGetValue(key, out string value))
                {
                    builder.Append(key).Append('=').Append(value).Append(Environment.NewLine);
                    remaining.Remove(key);
                    continue;
                }
            }

            builder.Append(line).Append(Environment.NewLine);
        }

        foreach (KeyValuePair<string, string> pair in remaining)
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append(Environment.NewLine);
        }

        return builder.ToString();
    }

    /// <summary>
    /// HotS Options dropdown is "AhliObs 0.75". The file is "AhliObs 0.75.StormInterface".
    /// Storing the filename with suffix makes the client look for a double-extension and fall back to Default.
    /// </summary>
    public static bool InterfaceNameEquals(string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual))
        {
            return false;
        }

        return string.Equals(
            StripStormInterface(expected),
            StripStormInterface(actual),
            StringComparison.OrdinalIgnoreCase
        );
    }

    public static string StripStormInterface(string name)
    {
        const string suffix = ".StormInterface";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^suffix.Length]
            : name;
    }
}
