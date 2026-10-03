using System;

namespace HeroesReplay.Core.Obs.Recording;

/// <summary>
/// A YouTube file is the match itself. Report scenes, the login form, and a
/// version-mismatch dialog are not a session.
/// </summary>
public static class MatchRecording
{
    public static readonly TimeSpan LoadingLimit = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan MinimumLength = TimeSpan.FromMinutes(2);

    public static bool ShouldStart(bool alreadyRecording, bool matchVisible) =>
        !alreadyRecording && matchVisible;

    public static bool ShouldPublish(int hudSamples, TimeSpan recordedFor) =>
        hudSamples > 0 && recordedFor >= MinimumLength;

    public static bool ShouldStopLoading(bool sawHud, TimeSpan loadingFor) =>
        !sawHud && loadingFor >= LoadingLimit;
}
