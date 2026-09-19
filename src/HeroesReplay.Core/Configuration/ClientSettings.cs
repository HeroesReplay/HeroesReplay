using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration;

/// <summary>
/// Heroes of the Storm client preset required for BitBlt/PrintWindow OCR and AhliObs panel hotkeys.
/// Written into Documents\Heroes of the Storm\Variables.txt.
/// displaymode 0 = windowed, 1 = fullscreen, 2 = borderless.
/// windowstate 1 = normal (not maximized).
/// </summary>
public class ClientSettings
{
    public string DisplayMode { get; set; } = "0";
    public string Width { get; set; } = "1920";
    public string Height { get; set; } = "1080";
    public string WindowWidth { get; set; } = "1920";
    public string WindowHeight { get; set; } = "1080";
    public string WindowState { get; set; } = "1";
    public string ObserverInterface { get; set; } = "AhliObs 0.75";
    public string ReplayInterface { get; set; } = "AhliObs 0.75";
    public string InterfaceFileName { get; set; } = "AhliObs 0.75.StormInterface";

    public IReadOnlyDictionary<string, string> VariablesPreset =>
        new Dictionary<string, string>
        {
            ["displaymode"] = DisplayMode,
            ["width"] = Width,
            ["height"] = Height,
            ["windowwidth"] = WindowWidth,
            ["windowheight"] = WindowHeight,
            ["windowstate"] = WindowState,
            ["observerinterface"] = ObserverInterface,
            ["replayinterface"] = ReplayInterface,
        };
}
