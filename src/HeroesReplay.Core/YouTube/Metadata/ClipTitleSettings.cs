namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// Clip titles (<c>YouTube:Titles:Clips</c>): a hook from the clip's own facts
/// (<see cref="ClipTitle"/>) instead of the flat <c>Hero - kind - Map - ReplayId</c>.
/// </summary>
public class ClipTitleSettings
{
    /// <summary>False writes the flat title, as before.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// One emoji on the templates that carry one (bsteps and pentakills). False keeps the words
    /// and drops the emoji.
    /// </summary>
    public bool Emoji { get; set; } = true;
}

/// <summary>
/// The game's name at the end of a title (<c>YouTube:Titles:GameName</c>):
/// <c>- Heroes of the Storm</c> when it fits, else <c>- HotS</c>, else nothing. It never pushes
/// another part of the title out.
/// </summary>
public class GameNameSettings
{
    public bool Clips { get; set; } = true;

    /// <summary>Full match titles. Off in code; the shipped appsettings turn it on.</summary>
    public bool FullMatches { get; set; }

    public string Name { get; set; } = "Heroes of the Storm";

    public string Short { get; set; } = "HotS";

    /// <summary>
    /// <paramref name="title"/> with the game's name appended when it fits in
    /// <paramref name="budget"/> characters: the full name first, then the short one.
    /// </summary>
    public string Append(string title, int budget)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        foreach (string name in new[] { Name, Short })
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string trimmed = name.Trim();
            if (HasWord(title, trimmed))
            {
                return title;
            }

            string candidate = title + " - " + trimmed;
            if (candidate.Length <= budget)
            {
                return candidate;
            }
        }

        return title;
    }

    /// <summary>
    /// The title already has the name as words of its own. "HotS" inside "skillshots" is not the
    /// game's name, and a title with that word still gets its suffix.
    /// </summary>
    private static bool HasWord(string title, string name)
    {
        int from = 0;
        while (from < title.Length)
        {
            int at = title.IndexOf(name, from, System.StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                return false;
            }

            int end = at + name.Length;
            bool startsWord = at == 0 || !char.IsLetterOrDigit(title[at - 1]);
            bool endsWord = end == title.Length || !char.IsLetterOrDigit(title[end]);
            if (startsWord && endsWord)
            {
                return true;
            }

            from = at + 1;
        }

        return false;
    }
}
