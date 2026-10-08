using System;

namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// The launch wait's pace. Every client screen (home, the login form, map loading, the game-data
/// dialogs, the game-launch failures and the MVP screen) comes from memory or the client's
/// windows (#292); the match clock is memory only. OCR reads the game window only for
/// Battle.net's own dialogs: the disconnect, the region and the region-version errors.
/// </summary>
public class OCRSettings
{
    public TimeSpan CheckSleepDuration { get; set; }
}
