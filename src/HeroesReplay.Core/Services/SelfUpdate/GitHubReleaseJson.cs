using System;
using System.Text.Json;

namespace HeroesReplay.Core.Services.SelfUpdate;

public readonly record struct ReleaseOffer(string Version, string DownloadUrl);

public static class GitHubReleaseJson
{
    public static ReleaseOffer? Read(string json, string assetName, string localVersion)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(assetName))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("tag_name", out JsonElement tagElement))
        {
            return null;
        }

        string tag = tagElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        if (
            string.Equals(
                tag,
                localVersion?.Trim(),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return null;
        }

        if (!document.RootElement.TryGetProperty("assets", out JsonElement assets))
        {
            return null;
        }

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString()
                : null;
            if (!string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string url = asset.TryGetProperty("browser_download_url", out JsonElement urlElement)
                ? urlElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            return new ReleaseOffer(tag, url);
        }

        return null;
    }
}
