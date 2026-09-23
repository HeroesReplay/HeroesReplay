using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;

namespace HeroesReplay.HeroesProfile.Client;

public partial class HeroesProfileClient
{
    public async Task<string> GetMmrTierAsync(
        string gameType,
        int mmr,
        CancellationToken cancellationToken = default
    )
    {
        RequestInformation request = CreateMmrTierRequest(RequestAdapter.BaseUrl, gameType, mmr);
        var errorMapping = new Dictionary<string, ParsableFactory<IParsable>>
        {
            {
                "401",
                global::HeroesReplay.HeroesProfile.Client.Models.Error.CreateFromDiscriminatorValue
            },
            {
                "403",
                global::HeroesReplay.HeroesProfile.Client.Models.Error.CreateFromDiscriminatorValue
            },
            {
                "404",
                global::HeroesReplay.HeroesProfile.Client.Models.Error.CreateFromDiscriminatorValue
            },
            {
                "422",
                global::HeroesReplay.HeroesProfile.Client.Models.Error.CreateFromDiscriminatorValue
            },
            {
                "429",
                global::HeroesReplay.HeroesProfile.Client.Models.Error.CreateFromDiscriminatorValue
            },
            {
                "500",
                global::HeroesReplay.HeroesProfile.Client.Models.Error.CreateFromDiscriminatorValue
            },
        };

        using Stream body = await RequestAdapter
            .SendPrimitiveAsync<Stream>(request, errorMapping, cancellationToken)
            .ConfigureAwait(false);
        if (body == null)
        {
            return null;
        }

        using var reader = new StreamReader(body);
        string text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return MmrTierPayload.Read(text);
    }

    public static RequestInformation CreateMmrTierRequest(string baseUrl, string gameType, int mmr)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentNullException(nameof(baseUrl));
        }

        var request = new RequestInformation(
            Method.GET,
            "{+baseurl}/mmr/tier{?game_type*,mmr*}",
            new Dictionary<string, object> { { "baseurl", baseUrl.TrimEnd('/') } }
        );
        request.Headers.TryAdd("Accept", "application/json");
        request.AddQueryParameters(new MmrTierQuery { GameType = gameType, Mmr = mmr });
        return request;
    }

    private sealed class MmrTierQuery
    {
        [QueryParameter("game_type")]
        public string GameType { get; set; }

        [QueryParameter("mmr")]
        public int Mmr { get; set; }
    }
}

public static class MmrTierPayload
{
    public static string Read(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        string trimmed = body.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(trimmed);
                if (
                    document.RootElement.TryGetProperty("tier", out JsonElement tier)
                    && tier.ValueKind == JsonValueKind.String
                )
                {
                    return BlankToNull(tier.GetString());
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2);
        }

        return BlankToNull(trimmed);
    }

    private static string BlankToNull(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
