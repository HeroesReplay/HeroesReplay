using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Twitch;

/// <summary>
/// What Twitch's token validator said about an access token. <see cref="Scopes"/> is set only
/// on a 200: space-separated, empty when the token has none. <see cref="Status"/> is null when
/// Twitch was not reached (no token, a network failure, the timeout).
/// </summary>
public sealed record TwitchTokenValidation(int? Status, string Scopes)
{
    public bool Known => Scopes != null;
}

/// <summary>The scopes an access token carries, read from <c>id.twitch.tv/oauth2/validate</c>.</summary>
public static class TwitchTokenScopes
{
    public const string ValidateUrl = "https://id.twitch.tv/oauth2/validate";

    public static async Task<TwitchTokenValidation> ReadAsync(
        string accessToken,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        HttpMessageHandler handler = null
    )
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new TwitchTokenValidation(null, null);
        }

        try
        {
            using HttpClient http =
                handler == null ? new HttpClient() : new HttpClient(handler, false);
            http.Timeout = timeout;
            using var request = new HttpRequestMessage(HttpMethod.Get, ValidateUrl);
            request.Headers.TryAddWithoutValidation("Authorization", "OAuth " + accessToken);
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                return new TwitchTokenValidation(status, null);
            }

            string body = await response
                .Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            return new TwitchTokenValidation(status, Parse(body));
        }
        catch (Exception e)
            when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new TwitchTokenValidation(null, null);
        }
    }

    internal static string Parse(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        if (
            !document.RootElement.TryGetProperty("scopes", out JsonElement scopes)
            || scopes.ValueKind != JsonValueKind.Array
        )
        {
            return string.Empty;
        }

        var names = new List<string>();
        foreach (JsonElement scope in scopes.EnumerateArray())
        {
            string value = scope.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                names.Add(value);
            }
        }

        return string.Join(' ', names);
    }
}
