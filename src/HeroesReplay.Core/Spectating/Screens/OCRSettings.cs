using System;

namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// What the spectator still reads with OCR, and shadow mode. The home screen, the map loading
/// screen and the match clock are memory only (#292).
/// </summary>
public class OCRSettings
{
    public TimeSpan CheckSleepDuration { get; set; }

    /// <summary>
    /// Shadow mode (#292): log the memory verdict next to every OCR screen verdict
    /// (<see cref="ScreenShadow"/>). It changes no decision.
    /// </summary>
    public bool ShadowEnabled { get; set; } = true;

    /// <summary>
    /// The least time between two saved frames of shadow disagreements on one state; empty or
    /// zero means <see cref="ScreenShadow.DefaultFrameInterval"/> (5 min). A shadow proof sets it
    /// short so every disagreement has a frame to classify (#292).
    /// </summary>
    public TimeSpan? ShadowFrameInterval { get; set; }
}
