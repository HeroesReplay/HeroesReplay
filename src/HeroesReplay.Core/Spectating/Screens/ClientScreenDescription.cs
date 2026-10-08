using System;
using System.Linq;
using HeroesClientSDK;

namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// One line for the log that names what HeroesClientSDK <see cref="ClientScreen"/> read: the
/// screen, the reason, the shown screens, whether a menu was seen, the dialogs, the game-launch
/// result and the build (#292).
/// </summary>
public static class ClientScreenDescription
{
    public static string Describe(ClientScreenSample? sample)
    {
        if (sample is not ClientScreenSample read)
        {
            return "not read";
        }

        string text =
            $"{read.Screen}, {read.Reason}, shown [{string.Join(",", (read.Shown ?? Array.Empty<string>()).Select(Short))}], menu seen {read.MenuSeen}";
        if (read.Dialogs is { Count: > 0 } dialogs)
        {
            text += $", dialogs [{string.Join(",", dialogs)}]";
        }

        if (read.LaunchResultCode is int code && code != 0)
        {
            text += $", launch result {code} {read.LaunchResult}";
        }

        // The build tells which client a read came from during a handoff, when the newest exe
        // and the replay's older build run side by side.
        if (read.ClientVersion != null)
        {
            text += $", build {read.ClientVersion}";
        }

        return text;
    }

    private static string Short(string screen) =>
        screen != null && screen.StartsWith("Screen", StringComparison.Ordinal)
            ? screen.Substring("Screen".Length)
            : screen;
}
