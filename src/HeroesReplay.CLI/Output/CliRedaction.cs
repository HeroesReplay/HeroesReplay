using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.ServiceHost.Logs;

namespace HeroesReplay.CLI.Output;

/// <summary>
/// Keeps secret values out of a command's output, text and JSON alike. Every secret this
/// process resolved (<see cref="Remember(AppSettings)"/>) is replaced wherever it appears, then
/// the token-shaped rules the role logs use (<see cref="ServiceLogRedaction"/>: <c>token=</c>,
/// <c>api_key=</c>, <c>key=</c>, <c>password=</c>, <c>Bearer</c>, <c>oauth:</c>, a Google API
/// key) run. A secret is reported as present or missing instead (<c>check config</c>).
/// </summary>
public sealed class CliRedaction
{
    public const string Mask = "[redacted]";

    // Shorter values (an empty password, "true") would mask ordinary words.
    private const int MinimumSecretLength = 6;

    private readonly object gate = new();
    private readonly HashSet<string> secrets = new(StringComparer.Ordinal);

    public void Remember(string secret)
    {
        string value = secret?.Trim();
        if (
            string.IsNullOrEmpty(value)
            || value.Length < MinimumSecretLength
            || value.StartsWith("op://", StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        lock (gate)
        {
            secrets.Add(value);
        }
    }

    /// <summary>Every secret <c>SecretResolver.Apply</c> resolves.</summary>
    public void Remember(AppSettings settings)
    {
        if (settings == null)
        {
            return;
        }

        Remember(settings.HeroesProfileApi?.ApiKey);
        Remember(settings.Twitch?.AccessToken);
        Remember(settings.Twitch?.RefreshToken);
        Remember(settings.Twitch?.ClientId);
        Remember(settings.TwitchExtension?.ApiKey);
        Remember(settings.OBS?.WebSocketPassword);
        Remember(settings.Github?.AccessToken);
        Remember(settings.YouTube?.ApiKey);
    }

    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        string[] known;
        lock (gate)
        {
            // Longest first, so a secret that contains another is masked whole.
            known = secrets.OrderByDescending(secret => secret.Length).ToArray();
        }

        foreach (string secret in known)
        {
            text = text.Replace(secret, Mask, StringComparison.Ordinal);
        }

        return ServiceLogRedaction.Redact(text);
    }
}
