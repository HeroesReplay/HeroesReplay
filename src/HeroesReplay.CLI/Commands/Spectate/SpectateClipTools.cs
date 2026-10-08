using System;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.Spectate;

/// <summary>
/// With clips on, spectate logs one error at start when ffmpeg or ffprobe cannot be found,
/// instead of a warning after the first pentakill. Spectate still runs.
/// </summary>
internal static class SpectateClipTools
{
    public static void Check(IServiceProvider services)
    {
        ILogger logger = services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(SpectateClipTools).FullName);
        ClipToolsStartup.Check(services.GetRequiredService<AppSettings>(), logger);
    }
}
