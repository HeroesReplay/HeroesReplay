using System;
using System.IO;
using System.Threading;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Twitch;
using Windows.Media.Ocr;

namespace HeroesReplay.CLI.Commands.Services;

internal static class ServiceRoleStartup
{
    public static ServiceStartupHandshake ForCurrentProcess(string exe)
    {
        AppSettings settings = ServiceCollectionExtensions.LoadAppSettings();
        ReadGrantedScopes(settings.Twitch);
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
            bool gameDataReady = ServiceRoleChecks.HeroesProfileGameDataReady(
                ServiceRoleChecks.HeroDataFilePresent(settings.HeroesDataPath),
                ServiceRoleChecks.MapCatalogPresent(settings.Maps)
            );
            ServiceRoleFacts facts = ServiceRoleChecks.Describe(
                exe,
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
                oauthPresent,
                gameDataReady
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

    /// <summary>
    /// Nothing configures Twitch:GrantedScopes, so the scopes come from the token itself. Only a
    /// role that uses chat, redemptions, or predictions needs them.
    /// </summary>
    private static void ReadGrantedScopes(TwitchSettings twitch)
    {
        if (
            twitch == null
            || twitch.GrantedScopes != null
            || !(
                twitch.EnableChatBot
                || twitch.EnablePubSub
                || twitch.EnableRequests
                || twitch.EnablePredictions
            )
        )
        {
            return;
        }

        TwitchTokenValidation read = TwitchTokenScopes
            .ReadAsync(twitch.AccessToken, TimeSpan.FromSeconds(10), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (read.Known)
        {
            twitch.GrantedScopes = read.Scopes;
            return;
        }

        Console.WriteLine(
            read.Status is int status
                ? $"Twitch did not validate the token (HTTP {status}). The twitch role starts anyway; a missing scope shows when it is used."
                : "Twitch scopes could not be read. The twitch role starts anyway; a missing scope shows when it is used."
        );
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
