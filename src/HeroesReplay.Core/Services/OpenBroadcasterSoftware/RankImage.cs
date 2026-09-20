using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Kiota.Abstractions.Serialization;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

public static class RankImage
{
    public static readonly string[] SourceNames =
    {
        "bronze-image",
        "silver-image",
        "gold-image",
        "platinum-image",
        "diamond-image",
        "master-image",
        "grandmaster-image",
    };

    public static string SourceName(string rank, int? leagueTier = null)
    {
        string key = Normalize(rank, leagueTier);
        return key == null ? null : key + "-image";
    }

    public static string FromAverageMmr(double? mmr)
    {
        if (!mmr.HasValue)
        {
            return null;
        }

        double value = mmr.Value;
        if (value < 1800)
        {
            return "Bronze";
        }

        if (value < 2100)
        {
            return "Silver";
        }

        if (value < 2400)
        {
            return "Gold";
        }

        if (value < 2700)
        {
            return "Platinum";
        }

        if (value < 3000)
        {
            return "Diamond";
        }

        if (value < 3300)
        {
            return "Master";
        }

        return "Grandmaster";
    }

    public static double? AveragePlayerMmr(UntypedNode players)
    {
        var values = new List<double>();
        CollectPlayerMmr(players, values);
        if (values.Count == 0)
        {
            return null;
        }

        double sum = 0;
        foreach (double value in values)
        {
            sum += value;
        }

        return sum / values.Count;
    }

    public static string Normalize(string rank, int? leagueTier)
    {
        if (!string.IsNullOrWhiteSpace(rank))
        {
            string token = rank.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            token = token.Trim().ToLowerInvariant();
            if (token is "woood" or "wood")
            {
                return "bronze";
            }

            if (token is "plat")
            {
                return "platinum";
            }

            if (token is "gm" or "grand")
            {
                return "grandmaster";
            }

            foreach (string source in SourceNames)
            {
                string name = source.Replace("-image", "", StringComparison.Ordinal);
                if (token == name || token.StartsWith(name, StringComparison.Ordinal))
                {
                    return name;
                }
            }
        }

        return leagueTier switch
        {
            1 => "bronze",
            2 => "silver",
            3 => "gold",
            4 => "platinum",
            5 => "diamond",
            6 => "master",
            7 => "grandmaster",
            _ => null,
        };
    }

    private static void CollectPlayerMmr(UntypedNode node, List<double> values)
    {
        if (node is UntypedArray array)
        {
            foreach (UntypedNode child in array.GetValue())
            {
                CollectPlayerMmr(child, values);
            }

            return;
        }

        if (node is not UntypedObject obj)
        {
            return;
        }

        IDictionary<string, UntypedNode> props = obj.GetValue();
        if (props.TryGetValue("player_mmr", out UntypedNode mmrNode))
        {
            double? parsed = ReadNumber(mmrNode);
            if (parsed.HasValue)
            {
                values.Add(parsed.Value);
            }

            return;
        }

        foreach (UntypedNode child in props.Values)
        {
            CollectPlayerMmr(child, values);
        }
    }

    private static double? ReadNumber(UntypedNode node)
    {
        if (node is UntypedDouble d)
        {
            return d.GetValue();
        }

        if (node is UntypedFloat f)
        {
            return f.GetValue();
        }

        if (node is UntypedInteger i)
        {
            return i.GetValue();
        }

        if (node is UntypedLong l)
        {
            return l.GetValue();
        }

        if (
            node is UntypedString s
            && double.TryParse(
                s.GetValue(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed
            )
        )
        {
            return parsed;
        }

        return null;
    }
}
