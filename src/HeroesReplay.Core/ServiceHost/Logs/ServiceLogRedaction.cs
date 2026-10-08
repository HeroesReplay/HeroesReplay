using System.Text.RegularExpressions;

namespace HeroesReplay.Core.ServiceHost.Logs;

/// <summary>
/// Hides tokens in text a role writes outside its own process: the heartbeat's <c>lastError</c>
/// and the role's log file. Both use these rules. <c>key=</c> covers the API key that a YouTube
/// upload URL carries (#368); a Google API key is also masked on its own shape, wherever it is.
/// </summary>
public static class ServiceLogRedaction
{
    private static readonly Regex SecretAssignment = new(
        @"(?i)\b(access_token|api_token|api_key|apikey|token|key|secret|password)=([^&\s""']+)",
        RegexOptions.Compiled
    );
    // "the v1 Bearer key" names a kind of credential; it is not one.
    private static readonly Regex BearerToken = new(
        @"(?i)\b(bearer|oauth:)\s*(?!(?:key|keys|token|tokens)\b)[A-Za-z0-9._\-]+",
        RegexOptions.Compiled
    );
    // Google API keys are "AIza" and 35 more URL-safe characters.
    private static readonly Regex GoogleApiKey = new(
        @"(?<![A-Za-z0-9_\-])AIza[A-Za-z0-9_\-]{35}(?![A-Za-z0-9_\-])",
        RegexOptions.Compiled
    );

    /// <summary>Replaces secret values, then cuts the text to <paramref name="maxLength"/>.</summary>
    public static string Redact(string text, int maxLength = int.MaxValue)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        string redacted = SecretAssignment.Replace(text, "$1=[redacted]");
        redacted = BearerToken.Replace(redacted, "$1 [redacted]");
        redacted = GoogleApiKey.Replace(redacted, "[redacted]");
        return redacted.Length <= maxLength ? redacted : redacted[..maxLength] + "...";
    }
}
