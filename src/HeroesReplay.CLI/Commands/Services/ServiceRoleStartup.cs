using System;
using System.IO;
using HeroesReplay.CLI;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Processes;
using HeroesReplay.Core.Services.Shared;
using Windows.Media.Ocr;

namespace HeroesReplay.CLI.Commands.Services;

internal static class ServiceRoleStartup
{
    public static ServiceStartupHandshake ForCurrentProcess(string exe)
    {
        AppSettings settings = ServiceCollectionExtensions.LoadAppSettings();
        object engine = TryCreateOcr();
        try
        {
            bool youtubeEnabled = settings.YouTube?.Enabled == true;
            bool contextWritable =
                !youtubeEnabled
                || ServiceRoleChecks.DirectoryWritable(
                    ServiceRoleChecks.ContextDirectory(settings)
                );
            bool oauthRequired = youtubeEnabled && settings.YouTube.DryRun == false;
            string secretsPath = ServiceRoleChecks.YouTubeSecretsPath(settings);
            bool oauthPresent = oauthRequired && secretsPath != null && File.Exists(secretsPath);
            ServiceRoleFacts facts = ServiceRoleChecks.Describe(
                exe,
                new AdminChecker().IsAdministrator(),
                engine != null ? new object() : null,
                settings.Capture != null
                    && ServiceRoleChecks.CaptureAvailable(settings.Capture.Method),
                ServiceRoleChecks.PathsExist(
                    settings.Location?.DataDirectory,
                    settings.Location?.GameInstallDirectory,
                    settings.Location?.BattlenetPath
                ),
                ServiceRoleChecks.ObsPrerequisites(
                    settings.OBS?.Enabled == true,
                    settings.OBS?.WebSocketEndpoint,
                    DefaultObsPath(settings.OBS?.ExecutablePath)
                ),
                settings.Twitch,
                ServiceRoleChecks.DirectoryWritable(ServiceRoleChecks.CacheDirectory(settings)),
                settings.HeroesProfileApi,
                settings.YouTube,
                contextWritable,
                oauthPresent
            );
            return new ServiceStartupHandshake
            {
                Spectate = facts.Spectate,
                Twitch = facts.Twitch,
                Download = facts.Download,
                YouTube = facts.YouTube,
                TryReadReady = record => ServiceReadyFile.TryRead(record),
                ReadyTimeout = TimeSpan.FromSeconds(45),
                PollInterval = TimeSpan.FromMilliseconds(200),
            };
        }
        finally
        {
            if (engine is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static string DefaultObsPath(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "obs-studio",
            "bin",
            "64bit",
            "obs64.exe"
        );
    }

    private static object TryCreateOcr()
    {
        try
        {
            return OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
