using System.Collections.Generic;

namespace HeroesReplay.Core.GameClient;

/// <summary>
/// Heroes of the Storm client preset required for frame capture, screen OCR, and AhliObs panel hotkeys.
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
    public const string AhliObsInterfaceFile = "AhliObs 0.75.StormInterface";

    /// <summary>
    /// AhliObs with hero names over the health bars and the chat hidden, built by
    /// tools/new-blind-interface.ps1 (#411).
    /// </summary>
    public const string AhliObsBlindInterfaceFile = "AhliObs 0.75 Blind.StormInterface";

    public string ObserverInterface { get; set; } = AhliObsInterfaceFile;
    public string ReplayInterface { get; set; } = AhliObsInterfaceFile;
    public string InterfaceFileName { get; set; } = AhliObsInterfaceFile;

    /// <summary>
    /// The archive a Blind Rating replay's client loads (#411). It is installed under
    /// <see cref="InterfaceFileName"/>, because the signed-in client keeps loading the interface
    /// it had even when Variables.txt names another one. Empty turns the blind HUD off.
    /// </summary>
    public string BlindInterfaceFileName { get; set; } = AhliObsBlindInterfaceFile;

    /// <summary>Play in Background. Account Variables.txt overrides the root file after login.</summary>
    public string SoundGlobal { get; set; } = "true";

    public IReadOnlyDictionary<string, string> DisplayPreset =>
        new Dictionary<string, string>
        {
            ["displaymode"] = DisplayMode,
            ["width"] = Width,
            ["height"] = Height,
            ["windowwidth"] = WindowWidth,
            ["windowheight"] = WindowHeight,
            ["windowstate"] = WindowState,
        };

    public IReadOnlyDictionary<string, string> InterfacePreset =>
        new Dictionary<string, string>
        {
            ["observerinterface"] = ObserverInterface,
            ["replayinterface"] = ReplayInterface,
        };

    public IReadOnlyDictionary<string, string> SoundPreset =>
        new Dictionary<string, string> { ["soundglobal"] = SoundGlobal };

    public IReadOnlyDictionary<string, string> VariablesPreset
    {
        get
        {
            var merged = new Dictionary<string, string>(DisplayPreset);
            foreach (KeyValuePair<string, string> pair in InterfacePreset)
            {
                merged[pair.Key] = pair.Value;
            }

            foreach (KeyValuePair<string, string> pair in SoundPreset)
            {
                merged[pair.Key] = pair.Value;
            }

            return merged;
        }
    }
}
