using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeroesReplay.Core.Dependencies;

/// <summary>
/// One external tool pinned in <c>dependencies.json</c>: the archive to download, its size and
/// SHA-256, and the files <c>deps install</c> takes out of it.
/// </summary>
public sealed record DependencyPin(
    string Name,
    string Version,
    string Source,
    string Url,
    long Size,
    string Sha256,
    IReadOnlyList<string> Files
);

/// <summary>
/// The pinned external tools, read from <c>src/HeroesReplay.Core/Dependencies/dependencies.json</c>,
/// which is embedded in this assembly. That file is the only place a tool's version, download
/// URL, and SHA-256 are written down.
/// </summary>
public static class DependencyManifest
{
    public const string FfmpegName = "ffmpeg";

    internal const string ResourceName = "HeroesReplay.Core.Dependencies.dependencies.json";

    private static readonly Lazy<IReadOnlyList<DependencyPin>> Embedded = new(LoadEmbedded);

    public static IReadOnlyList<DependencyPin> Pins => Embedded.Value;

    /// <summary>The pinned ffmpeg build: <c>ffmpeg.exe</c> and <c>ffprobe.exe</c>.</summary>
    public static DependencyPin Ffmpeg => Find(FfmpegName);

    public static DependencyPin Find(string name) =>
        Pins.FirstOrDefault(pin =>
            string.Equals(pin.Name, name, StringComparison.OrdinalIgnoreCase)
        ) ?? throw new InvalidOperationException($"{name} is not pinned in dependencies.json.");

    /// <summary>Reads and validates a manifest. Throws when an entry is incomplete.</summary>
    public static IReadOnlyList<DependencyPin> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var pins = new List<DependencyPin>();
        foreach (JsonElement tool in document.RootElement.GetProperty("tools").EnumerateArray())
        {
            var pin = new DependencyPin(
                Text(tool, "name"),
                Text(tool, "version"),
                Text(tool, "source"),
                Text(tool, "url"),
                tool.TryGetProperty("size", out JsonElement size) ? size.GetInt64() : 0,
                Text(tool, "sha256")?.ToLowerInvariant(),
                tool.TryGetProperty("files", out JsonElement files)
                    ? files.EnumerateArray().Select(file => file.GetString()).ToArray()
                    : Array.Empty<string>()
            );
            Validate(pin);
            pins.Add(pin);
        }

        return pins;
    }

    private static void Validate(DependencyPin pin)
    {
        string name = pin.Name ?? "(unnamed)";
        if (string.IsNullOrWhiteSpace(pin.Name) || pin.Name.IndexOfAny(BadNameChars) >= 0)
        {
            throw new InvalidDataException($"Pinned tool {name} needs a plain name.");
        }

        if (string.IsNullOrWhiteSpace(pin.Version))
        {
            throw new InvalidDataException($"Pinned tool {name} has no version.");
        }

        if (
            !Uri.TryCreate(pin.Url, UriKind.Absolute, out Uri url)
            || url.Scheme != Uri.UriSchemeHttps
        )
        {
            throw new InvalidDataException($"Pinned tool {name} needs an https url.");
        }

        if (pin.Sha256 == null || !Regex.IsMatch(pin.Sha256, "^[0-9a-f]{64}$"))
        {
            throw new InvalidDataException($"Pinned tool {name} needs a 64-digit hex sha256.");
        }

        if (pin.Size <= 0)
        {
            throw new InvalidDataException($"Pinned tool {name} needs the archive size.");
        }

        if (
            pin.Files.Count == 0
            || pin.Files.Any(file =>
                string.IsNullOrWhiteSpace(file)
                || file.IndexOfAny(BadNameChars) >= 0
                || file != Path.GetFileName(file)
            )
        )
        {
            throw new InvalidDataException($"Pinned tool {name} needs plain file names.");
        }
    }

    private static readonly char[] BadNameChars = Path.GetInvalidFileNameChars()
        .Concat(new[] { '/', '\\', ':' })
        .Distinct()
        .ToArray();

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<DependencyPin> LoadEmbedded()
    {
        using Stream stream =
            typeof(DependencyManifest).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not embedded.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
