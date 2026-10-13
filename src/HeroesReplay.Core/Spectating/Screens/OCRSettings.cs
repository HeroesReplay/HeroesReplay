using System;

namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// The launch wait's pace (<see cref="CheckSleepDuration"/>, between two passes of the launch
/// loop). The section keeps its old name so existing settings files still bind, but nothing in the
/// game client path is OCR'd any more (#292): every client screen and dialog, Battle.net's own
/// error dialogs included, comes from HeroesClientSDK memory reads or the client's windows, the
/// blank startup window from the captured frame's pixels, and the match clock from memory only.
/// </summary>
public class OCRSettings
{
    public TimeSpan CheckSleepDuration { get; set; }
}
