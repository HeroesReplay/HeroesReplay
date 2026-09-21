using System;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Connectivity;

public sealed class NetworkProbe : INetworkProbe
{
    private readonly HttpClient http;
    private readonly AppSettings settings;
    private readonly ILogger<NetworkProbe> logger;

    public NetworkProbe(HttpClient http, AppSettings settings, ILogger<NetworkProbe> logger)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ProbeInternetAsync(CancellationToken cancellationToken)
    {
        ConnectivitySettings connectivity = Settings;
        if (
            await PingHostAsync(connectivity.InternetHost, connectivity.ProbeTimeout)
                .ConfigureAwait(false)
        )
        {
            return true;
        }

        Uri fallback = new($"https://{connectivity.InternetHost}");
        return await ReachUriAsync(fallback, connectivity.ProbeTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> ProbeTwitchAsync(CancellationToken cancellationToken)
    {
        ConnectivitySettings connectivity = Settings;
        return ReachUriAsync(connectivity.TwitchUri, connectivity.ProbeTimeout, cancellationToken);
    }

    public Task<bool> ProbeHeroesProfileAsync(CancellationToken cancellationToken)
    {
        ConnectivitySettings connectivity = Settings;
        return ReachUriAsync(
            connectivity.HeroesProfileUri,
            connectivity.ProbeTimeout,
            cancellationToken
        );
    }

    private ConnectivitySettings Settings => settings.Connectivity ?? new ConnectivitySettings();

    private async Task<bool> PingHostAsync(string host, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        try
        {
            using var ping = new Ping();
            PingReply reply = await ping.SendPingAsync(host, (int)timeout.TotalMilliseconds)
                .ConfigureAwait(false);
            return reply.Status == IPStatus.Success;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "ICMP probe to {Host} failed.", host);
            return false;
        }
    }

    private async Task<bool> ReachUriAsync(
        Uri uri,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        if (uri == null)
        {
            return false;
        }

        if (await SendAsync(HttpMethod.Head, uri, timeout, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return await SendAsync(HttpMethod.Get, uri, timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> SendAsync(
        HttpMethod method,
        Uri uri,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            using var request = new HttpRequestMessage(method, uri);
            using HttpResponseMessage response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token
                )
                .ConfigureAwait(false);
            return (int)response.StatusCode < 500;
        }
        catch (Exception e)
            when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogDebug(e, "{Method} {Uri} probe failed.", method, uri);
            return false;
        }
    }
}
