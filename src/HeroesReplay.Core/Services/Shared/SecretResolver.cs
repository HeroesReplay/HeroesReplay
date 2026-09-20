using System;
using System.Diagnostics;
using HeroesReplay.Core.Configuration;

namespace HeroesReplay.Core.Services.Shared;

public static class SecretResolver
{
    public const string HeroesProfileApiKeyOpUri =
        "op://Heroes Replay/Heroes Profile API Key/password";

    public static void Apply(AppSettings settings)
    {
        if (settings == null)
        {
            return;
        }

        if (settings.HeroesProfileApi != null)
        {
            settings.HeroesProfileApi.ApiKey = Resolve(settings.HeroesProfileApi.ApiKey);
            settings.HeroesProfileApi.AwsAccessKey = Resolve(
                settings.HeroesProfileApi.AwsAccessKey
            );
            settings.HeroesProfileApi.AwsSecretKey = Resolve(
                settings.HeroesProfileApi.AwsSecretKey
            );
        }

        if (settings.Twitch != null)
        {
            settings.Twitch.AccessToken = Resolve(settings.Twitch.AccessToken);
            settings.Twitch.RefreshToken = Resolve(settings.Twitch.RefreshToken);
            settings.Twitch.ClientId = Resolve(settings.Twitch.ClientId);
        }

        if (settings.TwitchExtension != null)
        {
            settings.TwitchExtension.ApiKey = Resolve(settings.TwitchExtension.ApiKey);
        }

        if (settings.OBS != null)
        {
            settings.OBS.WebSocketPassword = Resolve(settings.OBS.WebSocketPassword);
        }

        if (settings.Github != null)
        {
            settings.Github.AccessToken = Resolve(settings.Github.AccessToken);
        }

        if (settings.YouTube != null)
        {
            settings.YouTube.ApiKey = Resolve(settings.YouTube.ApiKey);
        }
    }

    public static string Resolve(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        string trimmed = value.Trim();
        if (!trimmed.StartsWith("op://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return ReadOnePassword(trimmed);
    }

    public static string TryResolve(string value)
    {
        try
        {
            return Resolve(value);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string Describe(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "missing";
        }

        if (value.Trim().StartsWith("op://", StringComparison.OrdinalIgnoreCase))
        {
            return "1Password reference (unresolved)";
        }

        return $"set ({value.Trim().Length} chars)";
    }

    private static string ReadOnePassword(string uri)
    {
        var start = new ProcessStartInfo
        {
            FileName = "op",
            ArgumentList = { "read", uri },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        string token = ServiceAccountToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            start.Environment["OP_SERVICE_ACCOUNT_TOKEN"] = token;
        }

        using var process = Process.Start(start);
        if (process == null)
        {
            throw new InvalidOperationException(
                "Could not start the 1Password CLI (`op`). Install it and set OP_SERVICE_ACCOUNT."
            );
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignored
            }

            throw new TimeoutException($"Timed out reading 1Password secret `{uri}`.");
        }

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        string secret = stdout.Trim();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                $"1Password CLI failed for `{uri}` (exit {process.ExitCode}). {stderr.Trim()}"
            );
        }

        return secret;
    }

    private static string ServiceAccountToken()
    {
        string token = FirstNonEmpty(
            Environment.GetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN"),
            Environment.GetEnvironmentVariable("OP_SERVICE_ACCOUNT"),
            Environment.GetEnvironmentVariable(
                "OP_SERVICE_ACCOUNT_TOKEN",
                EnvironmentVariableTarget.User
            ),
            Environment.GetEnvironmentVariable("OP_SERVICE_ACCOUNT", EnvironmentVariableTarget.User)
        );
        return token;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
