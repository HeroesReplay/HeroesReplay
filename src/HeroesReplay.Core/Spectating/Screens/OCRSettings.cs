using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// Screen text the spectator reads with OCR: the home screen and the map loading screen.
/// The match clock is never read this way; it comes from memory.
/// </summary>
public class OCRSettings
{
    public IEnumerable<string> HomeScreenText { get; set; }
    public IEnumerable<string> LoadingScreenText { get; set; }
    public TimeSpan CheckSleepDuration { get; set; }

    /// <summary>
    /// Shadow mode (#292): log the memory verdict next to every OCR screen verdict
    /// (<see cref="ScreenShadow"/>). It changes no decision.
    /// </summary>
    public bool ShadowEnabled { get; set; } = true;
}
