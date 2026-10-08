namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>The game window's OCR text, as one short line for a log.</summary>
public static class WindowText
{
    public const int ExcerptLength = 160;

    /// <summary>One line of text for a log, at most <see cref="ExcerptLength"/> characters.</summary>
    public static string Excerpt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty)";
        }

        string line = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return line.Length <= ExcerptLength ? line : line.Substring(0, ExcerptLength) + "...";
    }
}
