using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util;
using Google.Apis.Util.Store;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.Core.YouTube;

/// <summary>
/// The youtube role's dependency probe (#305): refresh the stored upload OAuth token at Google's
/// token endpoint. That is not a YouTube Data API call, so it spends no quota unit and no upload
/// call (docs/youtube-uploader.md). The new access token is thrown away and the store is not
/// written. A refused refresh token (<c>invalid_grant</c>) or client is
/// <see cref="InvalidCode"/>; no stored consent is <see cref="MissingCode"/>; no answer is
/// <see cref="UnreachableCode"/>. No token reaches a cause or a log line.
/// </summary>
public sealed class YouTubeOAuthProbe : IServiceDependencyProbe
{
    public const string InvalidCode = "youtube.oauth_invalid";
    public const string MissingCode = "youtube.oauth_missing";
    public const string UnreachableCode = "youtube.oauth_unreachable";

    private const string Name = "YouTube OAuth";

    private const string UnreachableFix =
        "Nothing to do for a short outage: uploads wait and this clears when a probe passes (every 2 minutes while it fails). If it lasts, check that https://oauth2.googleapis.com answers from this machine.";

    private static readonly HttpClient Http = new();

    private readonly YouTubeSettings settings;
    private readonly Func<CancellationToken, Task<TokenResponse>> readStoredToken;
    private readonly Func<ClientSecrets> readSecrets;
    private readonly Func<RefreshTokenRequest, CancellationToken, Task<TokenResponse>> refresh;

    public YouTubeOAuthProbe(AppSettings settings)
        : this(
            settings?.YouTube,
            _ => ReadStoredTokenAsync(settings?.YouTube?.ChannelId),
            () => ReadSecrets(settings?.Location?.DataDirectory),
            RefreshAsync
        ) { }

    /// <param name="settings">Whether the uploader calls YouTube, and the channel.</param>
    /// <param name="readStoredToken">The token the authorization broker stored for the channel.</param>
    /// <param name="readSecrets"><c>client_secrets.json</c>, or null when it is missing.</param>
    /// <param name="refresh">The token endpoint call. Tests replace it.</param>
    public YouTubeOAuthProbe(
        YouTubeSettings settings,
        Func<CancellationToken, Task<TokenResponse>> readStoredToken,
        Func<ClientSecrets> readSecrets,
        Func<RefreshTokenRequest, CancellationToken, Task<TokenResponse>> refresh
    )
    {
        this.settings = settings;
        this.readStoredToken =
            readStoredToken ?? throw new ArgumentNullException(nameof(readStoredToken));
        this.readSecrets = readSecrets ?? throw new ArgumentNullException(nameof(readSecrets));
        this.refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
    }

    public string Dependency => Name;

    public string NotUsedReason
    {
        get
        {
            if (settings?.Enabled != true)
            {
                return "YouTube:Enabled is false, so the uploader does not call YouTube.";
            }

            return settings.DryRun
                ? "YouTube:DryRun is on, so the uploader does not call YouTube."
                : null;
        }
    }

    public ServiceDependencyResult Unreachable(string cause) =>
        ServiceDependencyResult.Unreachable(Name, UnreachableCode, cause, UnreachableFix);

    public async Task<ServiceDependencyResult> CheckAsync(CancellationToken cancellationToken)
    {
        string channel = settings?.ChannelId;
        ClientSecrets secrets = readSecrets();
        if (string.IsNullOrWhiteSpace(secrets?.ClientId))
        {
            return ServiceDependencyResult.Rejected(
                Name,
                MissingCode,
                "Location:DataDirectory\\client_secrets.json is missing or has no client id, so no upload can be authorized.",
                "Run `pwsh -File tools/fill-secrets-from-op.ps1`, which writes C:\\heroesreplay\\Data\\client_secrets.json, then restart the stack (`heroesreplay services stop`, then `heroesreplay services start --supervise`)."
            );
        }

        TokenResponse stored = await readStoredToken(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(stored?.RefreshToken))
        {
            return ServiceDependencyResult.Rejected(
                Name,
                MissingCode,
                $"No upload consent is stored for channel {channel} (%APPDATA%\\Google.Apis.Auth), so the uploader waits for a browser sign-in that nobody sees.",
                ConsentFix(channel)
            );
        }

        try
        {
            await refresh(
                    new RefreshTokenRequest
                    {
                        RefreshToken = stored.RefreshToken,
                        ClientId = secrets.ClientId,
                        ClientSecret = secrets.ClientSecret,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            return ServiceDependencyResult.Ok(
                Name,
                $"Google refreshed the upload token for channel {channel} (no YouTube quota spent)."
            );
        }
        catch (TokenResponseException e) when (Refused(e))
        {
            string description = string.IsNullOrWhiteSpace(e.Error?.ErrorDescription)
                ? string.Empty
                : ": " + ServiceHeartbeat.Redact(e.Error.ErrorDescription);
            return ServiceDependencyResult.Rejected(
                Name,
                InvalidCode,
                $"Google refused the stored upload consent for channel {channel} ({e.Error?.Error}{description}). It was revoked, it expired (7 days for an OAuth app in Testing), or client_secrets.json changed, so no recording can be uploaded.",
                ConsentFix(channel)
            );
        }
        catch (TokenResponseException e)
        {
            return Unreachable(
                $"Google's token endpoint answered {(e.StatusCode is HttpStatusCode status ? "HTTP " + (int)status : "an error")} ({e.Error?.Error})."
            );
        }
        catch (HttpRequestException e)
        {
            return Unreachable(
                "Google's token endpoint is not reachable: " + ServiceHeartbeat.Redact(e.Message)
            );
        }
    }

    /// <summary>The refresh token or the client was refused; a retry will not change it.</summary>
    private static bool Refused(TokenResponseException e)
    {
        string error = e.Error?.Error;
        if (
            string.Equals(error, "invalid_grant", StringComparison.Ordinal)
            || string.Equals(error, "invalid_client", StringComparison.Ordinal)
            || string.Equals(error, "unauthorized_client", StringComparison.Ordinal)
        )
        {
            return true;
        }

        return e.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized;
    }

    private static string ConsentFix(string channel) =>
        $"Grant the upload consent again at the machine: `heroesreplay services stop`, delete %APPDATA%\\Google.Apis.Auth\\Google.Apis.Auth.OAuth2.Responses.TokenResponse-{channel}, run `heroesreplay youtube uploader` by hand and sign in to the channel in the browser it opens, stop it with Ctrl+C, then `heroesreplay services start --supervise`. Recordings wait on disk until then.";

    private static async Task<TokenResponse> ReadStoredTokenAsync(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return null;
        }

        try
        {
            return await new FileDataStore(GoogleWebAuthorizationBroker.Folder)
                .GetAsync<TokenResponse>(channel)
                .ConfigureAwait(false);
        }
        catch (Exception e)
            when (e is IOException
                || e is UnauthorizedAccessException
                || e is Newtonsoft.Json.JsonException
            )
        {
            return null;
        }
    }

    private static ClientSecrets ReadSecrets(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return null;
        }

        string path = Path.Combine(dataDirectory, "client_secrets.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return GoogleClientSecrets.FromFile(path).Secrets;
        }
        catch (Exception e)
            when (e is IOException
                || e is UnauthorizedAccessException
                || e is Newtonsoft.Json.JsonException
            )
        {
            return null;
        }
    }

    private static Task<TokenResponse> RefreshAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken
    ) =>
        request.ExecuteAsync(
            Http,
            GoogleAuthConsts.OidcTokenUrl,
            cancellationToken,
            SystemClock.Default
        );
}
