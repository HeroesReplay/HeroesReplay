using System;
using System.Collections.Generic;
using System.Text;

namespace HeroesReplay.Core.GameClient;

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
    /// Variables.txt stores the interface file name, including .StormInterface.
    /// The Options dropdown hides that extension. The bare label is a different value.
    /// </summary>
    public static bool InterfaceNameEquals(string expected, string actual)
    {
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The client writes a border of a few pixels into windowwidth. 1921 is still 1920p.
    /// </summary>
    public static bool WindowEdgeEquals(string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (int.TryParse(expected, out int want) && int.TryParse(actual, out int got))
        {
            return Math.Abs(want - got) <= 8;
        }

        return false;
    }
}
