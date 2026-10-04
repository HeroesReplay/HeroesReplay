using System;
using System.IO;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.Spectate;

/// <summary>
/// Spectate drives OBS, so it writes the HeroesReplay release for the waiting scene when it starts. After a
/// release the new build's spectate is the first thing that runs, so the label changes with it.
/// </summary>
internal static class SpectateReleaseVersion
{
    public static void Write(IServiceProvider services)
    {
        ILogger logger = services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(SpectateReleaseVersion).FullName);
        try
        {
            string text = ReleaseVersionLabel.WriteForThisInstall(
                services.GetRequiredService<AppSettings>().Location?.DataDirectory
            );
            logger.LogInformation("The waiting scene shows HeroesReplay {Version}.", text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                e,
                "Could not write the HeroesReplay version for the waiting scene. Spectate continues."
            );
        }
    }
}
