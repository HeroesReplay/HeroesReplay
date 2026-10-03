using System.Text.RegularExpressions;

namespace HeroesReplay.Core.ServiceHost.Logs;

/// <summary>
/// Hides tokens in text a role writes outside its own process: the heartbeat's <c>lastError</c>
/// and the role's log file. Both use these rules.
/// </summary>
public static class ServiceLogRedaction
{
    private static readonly Regex SecretAssignment = new(
        @"(?i)\b(access_token|api_token|api_key|apikey|token|key|secret|password)=([^&\s""']+)",
        RegexOptions.Compiled
    );
    private static readonly Regex BearerToken = new(
        @"(?i)\b(bearer|oauth:)\s*[A-Za-z0-9._\-]+",
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
        return redacted.Length <= maxLength ? redacted : redacted[..maxLength] + "...";
    }
}
