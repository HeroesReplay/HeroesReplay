using System;
using HeroesReplay.Core.Analysis;

namespace HeroesReplay.Core.Spectating.Control;

/// <summary>
/// What the panel loop does in one tick: the panel to hide (its key is a toggle), whether that
/// panel was a chat request, so its request is marked hidden, and the panel to show.
/// </summary>
public readonly record struct PanelStep(Panel Hide, bool HiddenWasChat, Panel Show);

/// <summary>
/// Which observer panel is up, and whether a viewer asked for it. A chat request stays up for its
/// own duration whatever the automatic rule wants, and is marked hidden when it goes.
/// <para>
/// Before this class the loop kept one flag for both the panel on screen and the panel coming
/// next. A request that arrived while the automatic Talents panel was up spent the flag on hiding
/// Talents, so the requested panel was hidden a second later, never marked hidden, and chat was
/// told it was "already up" until another panel was requested.
/// </para>
/// </summary>
public sealed class PanelDirector
{
    private TimeSpan shown;

    /// <summary>The panel on screen, or <see cref="Panel.None"/>.</summary>
    public Panel Current { get; private set; } = Panel.None;

    /// <summary>A viewer asked for <see cref="Current"/>.</summary>
    public bool ChatRequested { get; private set; }

    /// <param name="request">The chat request consumed this tick, or <see cref="Panel.None"/>.</param>
    /// <param name="automatic">The panel the automatic rule wants now, or <see cref="Panel.None"/>.</param>
    /// <param name="chatDuration">How long a chat request stays up.</param>
    /// <param name="automaticLimit">How long <see cref="Current"/> may stay up when the rule put it there.</param>
    /// <param name="tick">The time since the last step.</param>
    public PanelStep Step(
        Panel request,
        Panel automatic,
        TimeSpan chatDuration,
        TimeSpan automaticLimit,
        TimeSpan tick
    )
    {
        Panel hide = Panel.None;
        bool hiddenWasChat = false;
        Panel show = Panel.None;
        bool visible = Current != Panel.None;

        if (request != Panel.None && request == Current)
        {
            // The rule's panel becomes the viewer's: it starts its own duration, no key sent.
            ChatRequested = true;
            shown = TimeSpan.Zero;
        }
        else
        {
            Panel next =
                request != Panel.None ? request
                : visible && ChatRequested ? Current
                : automatic;
            TimeSpan limit = ChatRequested ? chatDuration : automaticLimit;
            if (visible && (next != Current || shown >= limit))
            {
                hide = Current;
                hiddenWasChat = ChatRequested;
                bool expired = next == Current;
                Current = Panel.None;
                ChatRequested = false;
                shown = TimeSpan.Zero;
                visible = false;
                if (expired && hiddenWasChat)
                {
                    // The viewer's time is up: the rule decides again. A rule panel that reaches
                    // its limit is hidden and shown again in the same tick, as it always was.
                    next = automatic;
                }
            }

            if (!visible && next != Panel.None)
            {
                show = next;
                Current = next;
                ChatRequested = request != Panel.None;
                shown = TimeSpan.Zero;
            }
        }

        if (Current != Panel.None)
        {
            shown += tick;
        }

        return new PanelStep(hide, hiddenWasChat, show);
    }
}
