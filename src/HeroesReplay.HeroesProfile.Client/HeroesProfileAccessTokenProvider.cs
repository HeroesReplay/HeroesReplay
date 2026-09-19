using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions.Authentication;

namespace HeroesReplay.HeroesProfile.Client;

public sealed class HeroesProfileAccessTokenProvider : IAccessTokenProvider
{
    private readonly string token;

    public HeroesProfileAccessTokenProvider(string token, IEnumerable<string> allowedHosts = null)
    {
        this.token = token ?? string.Empty;
        AllowedHostsValidator = new AllowedHostsValidator(allowedHosts);
    }

    public AllowedHostsValidator AllowedHostsValidator { get; }

    public Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object> additionalAuthenticationContext = default,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(token);
    }
}
