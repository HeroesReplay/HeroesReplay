using System;

namespace HeroesReplay.Core.Spectating.Session;

/// <summary>
/// When the spectator shadows the end screen (#292): once the session is past the replay's
/// core-death time (the end-screen hold has started), until the session ends, at most once per
/// interval. Shadow only: it never ends or extends a session.
/// </summary>
public static class EndScreenShadow
{
    public static bool Due(
        bool pastCore,
        bool sessionEnding,
        DateTimeOffset now,
        DateTimeOffset nextShadow
    ) => pastCore && !sessionEnding && now >= nextShadow;
}
