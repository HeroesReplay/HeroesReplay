using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.HeroesProfile.Client;
using HeroesReplay.HeroesProfile.Client.Replays;
using Microsoft.Kiota.Abstractions;
using Polly.Timeout;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// The download role's dependency probe (#305): one Heroes Profile v1 replay-list call, the same
/// call that finds the newest replay id. A 401 or 403 is a rejected key; no answer, a timeout, a
/// 429, or a server error is unreachable. The key travels in the Authorization header and is
/// never put in a cause.
/// </summary>
public sealed class HeroesProfileApiProbe : IServiceDependencyProbe
{
    public const string RejectedCode = "download.heroesprofile_rejected";
    public const string UnreachableCode = "download.heroesprofile_unreachable";

    /// <summary>The probe's <see cref="ServiceDependencyResult.Dependency"/>.</summary>
    public const string DependencyName = "Heroes Profile API";

    private const string RejectedFix =
        "Check HeroesProfileApi:ApiKey: run `pwsh -File tools/fill-secrets-from-op.ps1` (or fix appsettings.secrets.json), confirm with `heroesreplay check heroesprofile`, then restart the stack (`heroesreplay services stop`, then `heroesreplay services start --supervise`; on the stream PC in a downtime). Until then the downloader stays up, degraded; the supervisor does not restart it.";

    private const string UnreachableFix =
        "Nothing to do for a short outage: the downloader keeps trying and this clears when a probe passes (every 2 minutes while it fails). If it lasts, check that https://www.heroesprofile.com answers and that HeroesProfileApi:ExternalV1BaseUri is right.";

    private readonly HeroesProfileApiSettings settings;
    private readonly Func<CancellationToken, Task<int?>> readMaxReplayId;

    public HeroesProfileApiProbe(HeroesProfileClient client, HeroesProfileApiSettings settings)
        : this(settings, token => ReadMaxReplayIdAsync(client, settings, token))
    {
        ArgumentNullException.ThrowIfNull(client);
    }

    /// <param name="settings">The key and the replay-list filters.</param>
    /// <param name="readMaxReplayId">The replay-list call. Tests replace it.</param>
    public HeroesProfileApiProbe(
        HeroesProfileApiSettings settings,
        Func<CancellationToken, Task<int?>> readMaxReplayId
    )
    {
        this.settings = settings;
        this.readMaxReplayId =
            readMaxReplayId ?? throw new ArgumentNullException(nameof(readMaxReplayId));
    }

    public string Dependency => DependencyName;

    public string NotUsedReason => null;

    public ServiceDependencyResult Unreachable(string cause) =>
        ServiceDependencyResult.Unreachable(DependencyName, UnreachableCode, cause, UnreachableFix);

    public async Task<ServiceDependencyResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings?.ApiKey))
        {
            return ServiceDependencyResult.Rejected(
                DependencyName,
                RejectedCode,
                "HeroesProfileApi:ApiKey is empty, so Heroes Profile refuses every call.",
                RejectedFix
            );
        }

        try
        {
            int? max = await readMaxReplayId(cancellationToken).ConfigureAwait(false);
            return ServiceDependencyResult.Ok(
                DependencyName,
                max is int id && id > 0
                    ? $"Heroes Profile accepted the API key (newest replay id {id})."
                    : "Heroes Profile accepted the API key."
            );
        }
        catch (ApiException e) when (e.ResponseStatusCode is 401 or 403)
        {
            return ServiceDependencyResult.Rejected(
                DependencyName,
                RejectedCode,
                $"Heroes Profile rejected HeroesProfileApi:ApiKey (HTTP {e.ResponseStatusCode}), so no replay can be listed or downloaded.",
                RejectedFix
            );
        }
        catch (ApiException e)
        {
            return Unreachable(
                $"Heroes Profile answered the replay list with HTTP {e.ResponseStatusCode}."
            );
        }
        catch (HttpRequestException e)
        {
            return Unreachable(
                "Heroes Profile is not reachable: " + ServiceHeartbeat.Redact(e.Message)
            );
        }
        catch (TimeoutRejectedException)
        {
            return Unreachable("Heroes Profile did not answer the replay list in time.");
        }
    }

    private static async Task<int?> ReadMaxReplayIdAsync(
        HeroesProfileClient client,
        HeroesProfileApiSettings settings,
        CancellationToken cancellationToken
    )
    {
        ReplaysGetResponse page = await client
            .Replays.GetAsync(
                config =>
                {
                    if (settings?.MinReplayId > 0)
                    {
                        config.QueryParameters.After = settings.MinReplayId;
                    }

                    string gameType = settings?.GameTypes?.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(gameType))
                    {
                        config.QueryParameters.GameType = gameType;
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        return page?.MaxReplayId;
    }
}
