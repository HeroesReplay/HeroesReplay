using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace HeroesReplay.Core.SelfUpdate;

/// <param name="Version">The release's tag.</param>
/// <param name="DownloadUrl">Where the release's zip is downloaded from.</param>
/// <param name="Sha256">
/// The zip's SHA-256 as GitHub recorded it at upload (lower-case hex), or null when the release
/// does not carry one.
/// </param>
public readonly record struct ReleaseOffer(string Version, string DownloadUrl, string Sha256 = null)
{
    /// <summary>
    /// True when the file is the zip the release published: its SHA-256 equals the recorded one.
    /// A release with no recorded digest cannot be checked and passes.
    /// </summary>
    public bool Matches(string zipPath)
    {
        if (string.IsNullOrEmpty(Sha256))
        {
            return true;
        }

        using FileStream file = File.OpenRead(zipPath);
        string actual = Convert.ToHexString(SHA256.HashData(file));
        return string.Equals(actual, Sha256, StringComparison.OrdinalIgnoreCase);
    }
}

public static class GitHubReleaseJson
{
    private const string Sha256Prefix = "sha256:";

    /// <summary>
    /// The update the latest release offers this install, or null when there is none.
    /// A release is offered only when it is newer than the installed one. The "latest" release is
    /// whatever GitHub marks so, which after a deleted or re-marked release can be an older build,
    /// and an older build must not replace a newer one by itself. When either version is not a
    /// release tag (<see cref="ReleaseVersion"/>) they cannot be ordered, and any different tag is
    /// offered, as it is with <paramref name="allowDowngrade"/>.
    /// </summary>
    public static ReleaseOffer? Read(
        string json,
        string assetName,
        string localVersion,
        bool allowDowngrade = false
    )
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

        if (string.Equals(tag, localVersion?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (
            !allowDowngrade
            && ReleaseVersion.TryParse(tag, out ReleaseVersion offered)
            && ReleaseVersion.TryParse(localVersion, out ReleaseVersion installed)
            && offered.CompareTo(installed) <= 0
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

            string digest =
                asset.TryGetProperty("digest", out JsonElement digestElement)
                && digestElement.ValueKind == JsonValueKind.String
                    ? digestElement.GetString()?.Trim()
                    : null;
            string sha256 =
                digest != null
                && digest.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase)
                && digest.Length > Sha256Prefix.Length
                    ? digest.Substring(Sha256Prefix.Length).ToLowerInvariant()
                    : null;
            return new ReleaseOffer(tag, url, sha256);
        }

        return null;
    }
}
