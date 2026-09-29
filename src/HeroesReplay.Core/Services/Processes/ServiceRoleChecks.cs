using System;
using System.IO;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Processes;

public sealed class SpectateStartupFacts
{
    public string LaunchPath { get; set; }
    public bool PrivilegeOk { get; set; }
    public object OcrResult { get; set; }
    public bool CaptureOk { get; set; }
    public bool PathsOk { get; set; }
    public bool ObsOk { get; set; }
}

public sealed class TwitchStartupFacts
{
    public bool TokenOk { get; set; }
    public bool ScopesOk { get; set; }
    public bool RewardsOk { get; set; }
    public bool PredictionsOk { get; set; }
}

public sealed class DownloadStartupFacts
{
    public bool CredentialOk { get; set; }
    public bool CacheWritable { get; set; }

    /// <summary>Null skips the check. False fails startup. Callers must not download to produce this.</summary>
    public bool? GameDataReady { get; set; }
}

public sealed class YouTubeStartupFacts
{
    public bool Enabled { get; set; }
    public bool ContextWritable { get; set; }
    public bool OAuthRequired { get; set; }
    public bool OAuthOk { get; set; }
}

public sealed class ServiceRoleFacts
{
    public SpectateStartupFacts Spectate { get; set; }
    public TwitchStartupFacts Twitch { get; set; }
    public DownloadStartupFacts Download { get; set; }
    public YouTubeStartupFacts YouTube { get; set; }
}

public static class ServiceRoleChecks
{
    public static ServiceRoleFacts Describe(
        string launchPath,
        bool privilegeOk,
        object ocrResult,
        bool captureOk,
        bool pathsOk,
        bool obsOk,
        TwitchSettings twitch,
        bool cacheWritable,
        HeroesProfileApiSettings heroesProfile,
        YouTubeSettings youtube,
        bool contextWritable,
        bool oauthPresent
    )
    {
        return new ServiceRoleFacts
        {
            Spectate = new SpectateStartupFacts
            {
                LaunchPath = launchPath,
                PrivilegeOk = privilegeOk,
                OcrResult = ocrResult,
                CaptureOk = captureOk,
                PathsOk = pathsOk,
                ObsOk = obsOk,
            },
            Twitch = TwitchFrom(twitch),
            Download = new DownloadStartupFacts
            {
                CredentialOk = HasSecret(heroesProfile?.ApiKey),
                CacheWritable = cacheWritable,
            },
            YouTube = YouTubeFrom(youtube, contextWritable, oauthPresent),
        };
    }

    // Supplied results only. Does not call Helix, EventSub, or the Twitch token validator.
    public static TwitchStartupFacts TwitchFrom(TwitchSettings twitch)
    {
        bool tokenOk = HasSecret(twitch?.AccessToken) && HasSecret(twitch?.ClientId);
        bool rewardsRequired = twitch != null && (twitch.EnablePubSub || twitch.EnableRequests);
        bool predictionsRequired = twitch != null && twitch.EnablePredictions;
        return new TwitchStartupFacts
        {
            TokenOk = tokenOk,
            ScopesOk = tokenOk,
            RewardsOk = !rewardsRequired || tokenOk,
            PredictionsOk = !predictionsRequired || tokenOk,
        };
    }

    public static YouTubeStartupFacts YouTubeFrom(
        YouTubeSettings youtube,
        bool contextWritable,
        bool oauthPresent
    )
    {
        bool enabled = youtube?.Enabled == true;
        bool oauthRequired = enabled && youtube.DryRun == false;
        return new YouTubeStartupFacts
        {
            Enabled = enabled,
            ContextWritable = contextWritable,
            OAuthRequired = oauthRequired,
            OAuthOk = oauthPresent,
        };
    }

    public static string SpectateFailure(string launchPath, SpectateStartupFacts facts)
    {
        if (facts == null)
        {
            return null;
        }

        string path = string.IsNullOrWhiteSpace(facts.LaunchPath) ? launchPath : facts.LaunchPath;
        if (!ServiceProcessPlan.IsHeroesReplay(Path.GetFileName(path ?? string.Empty)))
        {
            return "launch path is not heroesreplay.";
        }

        if (!facts.PrivilegeOk)
        {
            return "privilege check failed.";
        }

        if (facts.OcrResult == null)
        {
            return "OCR engine was not created.";
        }

        if (!facts.CaptureOk)
        {
            return "capture is not available.";
        }

        if (!facts.PathsOk)
        {
            return "game or data paths are not available.";
        }

        if (!facts.ObsOk)
        {
            return "OBS prerequisites failed.";
        }

        return null;
    }

    public static string TwitchFailure(TwitchStartupFacts facts)
    {
        if (facts == null)
        {
            return null;
        }

        if (!facts.TokenOk)
        {
            return "Twitch token or client id is missing.";
        }

        if (!facts.ScopesOk)
        {
            return "Twitch scopes are not valid.";
        }

        if (!facts.RewardsOk)
        {
            return "Twitch reward access is not valid.";
        }

        if (!facts.PredictionsOk)
        {
            return "Twitch prediction access is not valid.";
        }

        return null;
    }

    public static string DownloadFailure(DownloadStartupFacts facts)
    {
        if (facts == null)
        {
            return null;
        }

        if (!facts.CredentialOk)
        {
            return "Heroes Profile credential is missing.";
        }

        if (!facts.CacheWritable)
        {
            return "Heroes Profile cache is not writable.";
        }

        if (facts.GameDataReady == false)
        {
            return "Heroes Profile game data is not ready.";
        }

        return null;
    }

    public static string YouTubeFailure(YouTubeStartupFacts facts)
    {
        if (facts == null || !facts.Enabled)
        {
            return null;
        }

        if (!facts.ContextWritable)
        {
            return "context directory is not writable.";
        }

        if (facts.OAuthRequired && !facts.OAuthOk)
        {
            return "OAuth client secrets are missing.";
        }

        return null;
    }

    public static bool CaptureAvailable(CaptureMethod method)
    {
        return method == CaptureMethod.PrintWindow || method == CaptureMethod.BitBlt;
    }

    public static bool PathsExist(
        string dataDirectory,
        string gameInstallDirectory,
        string battlenetPath
    )
    {
        return !string.IsNullOrWhiteSpace(dataDirectory)
            && Directory.Exists(dataDirectory)
            && !string.IsNullOrWhiteSpace(gameInstallDirectory)
            && Directory.Exists(gameInstallDirectory)
            && !string.IsNullOrWhiteSpace(battlenetPath)
            && File.Exists(battlenetPath);
    }

    public static bool ObsPrerequisites(bool enabled, string endpoint, string executablePath)
    {
        if (!enabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        return File.Exists(executablePath);
    }

    public static string CacheDirectory(AppSettings settings)
    {
        string data = settings?.Location?.DataDirectory;
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        string name = settings.HeroesProfileApi?.StandardCacheDirectoryName;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Standard";
        }

        return Path.Combine(data, name);
    }

    public static string ContextDirectory(AppSettings settings)
    {
        string data = settings?.Location?.DataDirectory;
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        return Path.Combine(data, "Contexts");
    }

    public static string YouTubeSecretsPath(AppSettings settings)
    {
        string data = settings?.Location?.DataDirectory;
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        return Path.Combine(data, "client_secrets.json");
    }

    public static bool DirectoryWritable(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        string probe = Path.Combine(
            directory,
            ".heroesreplay-write-probe-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            Directory.CreateDirectory(directory);
            using (
                var stream = new FileStream(
                    probe,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                )
            )
            {
                stream.WriteByte(0);
            }

            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            try
            {
                if (File.Exists(probe))
                {
                    File.Delete(probe);
                }
            }
            catch (Exception)
            {
                // The writable probe is not MinReplayId and is best-effort to remove.
            }

            return false;
        }
    }

    private static bool HasSecret(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !value.Trim().StartsWith("op://", StringComparison.OrdinalIgnoreCase);
    }
}
