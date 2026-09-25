using System;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// End-of-replay teardown. A hung game window must not be captured:
/// PrintWindow on that HWND blocks, so Kill would never run and spectate
/// would stay up.
/// </summary>
public static class ReplayShutdown
{
    public static void CaptureEndThenKill(IGameController game, ILogger logger)
    {
        if (game == null)
        {
            throw new ArgumentNullException(nameof(game));
        }

        if (game.IsGameHung())
        {
            logger?.LogWarning("Skipping end screenshot because the game window is hung.");
        }
        else
        {
            try
            {
                game.SaveEndScreenshot();
            }
            catch (Exception exception)
            {
                logger?.LogWarning(exception, "Could not save end screenshot.");
            }
        }

        game.Kill();
    }
}
