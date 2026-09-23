using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.HeroesProfileExtension;

public class TwitchExtensionService : ITwitchExtensionService
{
    public const string HttpClientName = "HeroesProfileTwitchExtension";
    public const string UploaderKeyHeader = "X-HP-Twitch-Key";

    private readonly ILogger<TwitchExtensionService> logger;
    private readonly HttpClient httpClient;
    private readonly AppSettings settings;

    public TwitchExtensionService(
        ILogger<TwitchExtensionService> logger,
        HttpClient httpClient,
        AppSettings settings
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.httpClient.Timeout = TimeSpan.FromSeconds(60);
        if (settings.HeroesProfileApi?.TwitchBaseUri != null)
        {
            this.httpClient.BaseAddress = settings.HeroesProfileApi.TwitchBaseUri;
        }
    }

    public async Task<ExtensionPostOutcome> PostSnapshotAsync(
        string gameId,
        int seq,
        ExtensionSnapshot snapshot,
        CancellationToken token = default
    )
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (!TryPrepare(out string key))
        {
            return ExtensionPostOutcome.Stopped;
        }

        string json = JsonSerializer.Serialize(
            new SnapshotBody
            {
                GameId = gameId,
                Seq = seq,
                Phase = snapshot.Phase,
                GameMode = snapshot.GameMode,
                Map = snapshot.Map,
                GameVersion = snapshot.GameVersion,
                Players = snapshot.Players,
            },
            ExtensionSnapshotSelector.Json
        );

        for (int attempt = 0; attempt < 4; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "uploader/snapshot");
            AddKey(request, key);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Twitch extension snapshot failed.");
                if (attempt >= 2)
                {
                    return ExtensionPostOutcome.Failed;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                int code = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation(
                        "Twitch extension snapshot {Seq} {Phase} accepted.",
                        seq,
                        snapshot.Phase
                    );
                    return ExtensionPostOutcome.Sent;
                }

                string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (code is 401 or 402 or 422)
                {
                    logger.LogWarning(
                        "Twitch extension stopped: HTTP {Status} {Message}",
                        code,
                        ReadMessage(text) ?? Trim(text)
                    );
                    return ExtensionPostOutcome.Stopped;
                }

                if (code == 429 && attempt < 3)
                {
                    TimeSpan wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);
                    logger.LogWarning(
                        "Twitch extension snapshot rate limited. Retrying in {Wait}.",
                        wait
                    );
                    await Task.Delay(wait, token).ConfigureAwait(false);
                    continue;
                }

                if (code >= 500 && attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                    continue;
                }

                logger.LogWarning(
                    "Twitch extension snapshot refused: HTTP {Status} {Body}",
                    code,
                    Trim(text)
                );
                return ExtensionPostOutcome.Failed;
            }
        }

        return ExtensionPostOutcome.Failed;
    }

    public async Task<ExtensionWhoAmI> WhoAmIAsync(CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(settings.TwitchExtension?.ApiKey))
        {
            return new ExtensionWhoAmI
            {
                Reachable = false,
                Message =
                    "Uploader key is missing. Set TwitchExtension:ApiKey to op://Heroes Replay/Heroes Profile Twitch Uploader Key/password.",
            };
        }

        if (!TryPrepare(out string key))
        {
            return new ExtensionWhoAmI
            {
                Reachable = false,
                Message = "HeroesProfileApi:TwitchBaseUri is missing.",
            };
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "uploader/whoami");
            AddKey(request, key);
            using HttpResponseMessage response = await httpClient
                .SendAsync(request, token)
                .ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new ExtensionWhoAmI
                {
                    Reachable = false,
                    StatusCode = (int)response.StatusCode,
                    Message =
                        ReadMessage(body)
                        ?? $"Heroes Profile answered HTTP {(int)response.StatusCode}.",
                };
            }

            WhoAmIBody parsed = JsonSerializer.Deserialize<WhoAmIBody>(
                body,
                ExtensionSnapshotSelector.Json
            );
            return new ExtensionWhoAmI
            {
                Reachable = true,
                StatusCode = (int)response.StatusCode,
                TwitchLogin = parsed?.TwitchLogin,
                TwitchDisplayName = parsed?.TwitchDisplayName,
                PlayerLinked = parsed?.PlayerLinked ?? false,
                EntitlementActive = parsed?.Entitlement?.Active ?? false,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Twitch extension whoami failed.");
            return new ExtensionWhoAmI
            {
                Reachable = false,
                Message = "Could not reach Heroes Profile.",
            };
        }
    }

    private bool TryPrepare(out string key)
    {
        key = settings.TwitchExtension?.ApiKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            logger.LogWarning("Twitch extension uploader key is missing.");
            return false;
        }

        if (httpClient.BaseAddress == null)
        {
            logger.LogWarning("Twitch extension base address is missing.");
            return false;
        }

        return true;
    }

    private static void AddKey(HttpRequestMessage request, string key)
    {
        request.Headers.TryAddWithoutValidation(UploaderKeyHeader, key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static string ReadMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (
                document.RootElement.TryGetProperty("message", out JsonElement message)
                && message.ValueKind == JsonValueKind.String
            )
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string Trim(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text.Length <= 300 ? text : text.Substring(0, 300);
    }

    private sealed class SnapshotBody
    {
        public string GameId { get; set; }
        public int Seq { get; set; }
        public string Phase { get; set; }
        public string GameMode { get; set; }
        public string Map { get; set; }
        public string GameVersion { get; set; }
        public System.Collections.Generic.IReadOnlyList<ExtensionSnapshotPlayer> Players { get; set; }
    }

    private sealed class WhoAmIBody
    {
        public string TwitchLogin { get; set; }
        public string TwitchDisplayName { get; set; }
        public bool PlayerLinked { get; set; }
        public EntitlementBody Entitlement { get; set; }
    }

    private sealed class EntitlementBody
    {
        public bool Active { get; set; }
    }
}
