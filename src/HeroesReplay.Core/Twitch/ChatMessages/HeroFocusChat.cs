using HeroesReplay.Core.HeroFocus.Picking;
using HeroesReplay.Core.Status;

namespace HeroesReplay.Core.Twitch.ChatMessages;

/// <summary>
/// One chat line per focus replay (#433), once its match clock runs, so a replay that never
/// reaches the match is not announced: the hero and build, never the player.
/// </summary>
public sealed class HeroFocusChat
{
    private int? announced;

    /// <summary>The line to send for this status, once per replay; null otherwise.</summary>
    public string Next(SpectatorStatus status)
    {
        SpectatorHeroFocusStatus focus = status?.HeroFocus;
        if (
            focus == null
            || status.SnapshotStale
            || !status.SpectatorRunning
            || status.Phase != "TimerDetected"
            || status.ReplayId != focus.ReplayId
            || announced == focus.ReplayId
        )
        {
            return null;
        }

        announced = focus.ReplayId;
        return Text(focus);
    }

    public static string Text(SpectatorHeroFocusStatus focus) =>
        $"Hero focus: this game follows {FocusPick.Describe(focus.Hero, focus.Build)}.";
}
