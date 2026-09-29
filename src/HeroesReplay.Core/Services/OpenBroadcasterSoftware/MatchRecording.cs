using System;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

/// <summary>
/// A YouTube file is the match itself. Report scenes, the login form, and a
/// version-mismatch dialog are not a session.
/// </summary>
public static class MatchRecording
{
    public static readonly TimeSpan LoadingLimit = TimeSpan.FromMinutes(3);

    public static bool ShouldStart(bool alreadyRecording, bool matchVisible) =>
        !alreadyRecording && matchVisible;

    public static bool ShouldPublish(int hudSamples) => hudSamples > 0;

    public static bool ShouldStopLoading(bool sawHud, TimeSpan loadingFor) =>
        !sawHud && loadingFor >= LoadingLimit;
}
