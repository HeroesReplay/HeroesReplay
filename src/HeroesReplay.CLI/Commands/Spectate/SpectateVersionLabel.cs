using System;
using System.IO;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.Spectate;

/// <summary>
/// Spectate drives OBS, so it writes the waiting scene's version label when it starts. After a
/// release the new build's spectate is the first thing that runs, so the label changes with it.
/// </summary>
internal static class SpectateVersionLabel
{
    public static void Write(IServiceProvider services)
    {
        ILogger logger = services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(SpectateVersionLabel).FullName);
        try
        {
            string text = ObsVersionLabel.WriteForThisInstall(
                services.GetRequiredService<AppSettings>().Location?.DataDirectory
            );
            logger.LogInformation("OBS waiting scene version label: {Version}.", text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not write the OBS version label. Spectate continues.");
        }
    }
}
