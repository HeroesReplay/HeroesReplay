namespace HeroesReplay.Core.Models;

public enum CaptureMethod
{
    /// <summary>
    /// Copies the desktop pixels at the game window. A window covering the game is included.
    /// </summary>
    BitBlt = 1,

    /// <summary>
    /// The game window's composed frame. Other windows on the desktop are ignored.
    /// </summary>
    PrintWindow = 2,

    /// <summary>
    /// Stub the entire capture process.
    /// </summary>
    None = 3,
}
