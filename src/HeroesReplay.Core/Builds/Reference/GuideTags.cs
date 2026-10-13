using System;

namespace HeroesReplay.Core.Builds.Reference;

/// <summary>The tags a guide puts on a build, and the order a tie between two builds is broken in.</summary>
public static class GuideTags
{
    public const string Recommended = "Recommended";
    public const string Advanced = "Advanced";
    public const string Situational = "Situational";
    public const string Beginner = "Beginner";
    public const string Aram = "ARAM";
    public const string NotRecommended = "Not recommended";

    private static readonly string[] Order =
    {
        Recommended,
        Advanced,
        Situational,
        Beginner,
        Aram,
        NotRecommended,
    };

    public static bool IsAram(string tag) =>
        string.Equals(tag, Aram, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lower ranks first: Recommended wins a tie, then Advanced, and so on. An unknown tag is last.</summary>
    public static int Rank(string tag)
    {
        for (int i = 0; i < Order.Length; i++)
        {
            if (string.Equals(Order[i], tag, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return Order.Length;
    }
}
