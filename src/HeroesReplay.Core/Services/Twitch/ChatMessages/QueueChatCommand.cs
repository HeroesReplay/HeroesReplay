using System;

namespace HeroesReplay.Core.Services.Twitch.ChatMessages;

public enum QueueChatAction
{
    None,
    Count,
    Mine,
    Remove,
    At,
}

public static class QueueChatCommand
{
    public static bool TryRead(string text, out QueueChatAction action, out int position)
    {
        action = QueueChatAction.None;
        position = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !IsQueueWord(parts[0]))
        {
            return false;
        }

        if (parts.Length == 1)
        {
            action = QueueChatAction.Count;
            return true;
        }

        if (parts.Length == 2 && parts[1].Equals("me", StringComparison.OrdinalIgnoreCase))
        {
            action = QueueChatAction.Mine;
            return true;
        }

        if (parts.Length == 2 && parts[1].Equals("remove", StringComparison.OrdinalIgnoreCase))
        {
            action = QueueChatAction.Remove;
            return true;
        }

        if (parts.Length == 2 && int.TryParse(parts[1], out int index) && index > 0)
        {
            action = QueueChatAction.At;
            position = index;
            return true;
        }

        return false;
    }

    private static bool IsQueueWord(string word)
    {
        return word.Equals("!requests", StringComparison.OrdinalIgnoreCase)
            || word.Equals("!queue", StringComparison.OrdinalIgnoreCase);
    }
}
