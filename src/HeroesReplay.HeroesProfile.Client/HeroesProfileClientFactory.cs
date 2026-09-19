using System;
using System.Net.Http;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace HeroesReplay.HeroesProfile.Client;

public static class HeroesProfileClientFactory
{
    public static readonly Uri DefaultBaseUri = new Uri(
        "https://www.heroesprofile.com/api/external/v1"
    );

    public static HeroesProfileClient Create(string apiKey, Uri baseUri = null)
    {
        Uri resolved = baseUri ?? DefaultBaseUri;
        IAuthenticationProvider authentication = string.IsNullOrWhiteSpace(apiKey)
            ? new AnonymousAuthenticationProvider()
            : new BaseBearerTokenAuthenticationProvider(
                new HeroesProfileAccessTokenProvider(apiKey, new[] { resolved.Host })
            );

        var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var adapter = new HttpClientRequestAdapter(authentication, httpClient: httpClient)
        {
            BaseUrl = resolved.ToString().TrimEnd('/'),
        };

        return new HeroesProfileClient(adapter);
    }
}
