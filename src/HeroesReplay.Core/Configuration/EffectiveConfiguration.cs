using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HeroesReplay.Core.ServiceHost.Logs;
using Microsoft.Extensions.Configuration;

namespace HeroesReplay.Core.Configuration;

/// <summary>
/// One configuration layer, lowest first: <see cref="ConfigurationLayers.Base"/>
/// (<c>appsettings.json</c>), <see cref="ConfigurationLayers.Secrets"/>
/// (<c>appsettings.secrets.json</c>), <see cref="ConfigurationLayers.Overlay"/>
/// (<c>appsettings.{env}.json</c>), then <see cref="ConfigurationLayers.Environment"/>
/// (<c>HEROES_REPLAY_</c> variables). A later layer wins.
/// </summary>
/// <param name="Present">The file exists (always true for the variables).</param>
/// <param name="Keys">How many keys with a value this layer sets.</param>
public sealed record ConfigurationLayer(
    int Order,
    string Layer,
    string Source,
    bool Present,
    int Keys
);

/// <summary>
/// One effective key: its value, or <c>(set)</c> / <c>(empty)</c> / <c>(op:// reference)</c>
/// when <see cref="Redacted"/>; the layer and source that won; and the lower sources it
/// overrides. No value of an overridden layer is kept.
/// </summary>
public sealed record EffectiveSetting(
    string Key,
    string Value,
    bool Redacted,
    string Layer,
    string Source,
    IReadOnlyList<string> Overrides
);

/// <summary>What <c>heroesreplay config effective</c> prints, as text or JSON.</summary>
public sealed record EffectiveConfiguration
{
    public int SchemaVersion => 1;
    public bool Ok { get; init; }

    /// <summary>Null when <see cref="Ok"/>; otherwise <c>config.base_missing</c>, <c>config.unreadable</c>, or <c>config.section_not_found</c>.</summary>
    public string Code { get; init; }
    public string Message { get; init; }

    /// <summary>The overlay name (<c>prod</c>, <c>dev</c>), or null when no overlay applies.</summary>
    public string Environment { get; init; }

    /// <summary>Where <see cref="Environment"/> came from: <c>--environment</c>, <c>HEROES_REPLAY_ENV</c>, or <c>none</c>.</summary>
    public string EnvironmentSource { get; init; }
    public string BasePath { get; init; }
    public string Section { get; init; }
    public IReadOnlyList<ConfigurationLayer> Layers { get; init; } = [];
    public int RedactedCount { get; init; }
    public IReadOnlyList<EffectiveSetting> Settings { get; init; } = [];
}

/// <summary>The layer names, lowest first.</summary>
public static class ConfigurationLayers
{
    public const string Base = "base";
    public const string Secrets = "secrets";
    public const string Overlay = "overlay";
    public const string Environment = "environment";
}

/// <summary>A configuration provider and the layer it is.</summary>
public sealed record ConfigurationLayerSource(string Layer, string Source, bool Present);

/// <summary>
/// The effective settings of a configuration root, each with the provider that won, and
/// every secret redacted (<see cref="ConfigurationRedaction"/>). Nothing here resolves an
/// <c>op://</c> reference or reads a value that a lower layer overrode.
/// </summary>
public static class ConfigurationProvenance
{
    public const string BaseMissing = "config.base_missing";
    public const string Unreadable = "config.unreadable";
    public const string SectionNotFound = "config.section_not_found";

    /// <param name="root">The configuration, built as the roles build it.</param>
    /// <param name="label">The layer of each of <paramref name="root"/>'s providers.</param>
    /// <param name="section">Only keys at or under this section (<c>OBS</c>, <c>Twitch:Predictions</c>). Null for all.</param>
    public static EffectiveConfiguration Describe(
        IConfigurationRoot root,
        Func<IConfigurationProvider, ConfigurationLayerSource> label,
        string section = null
    )
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(label);
        List<(IConfigurationProvider Provider, ConfigurationLayerSource Source)> providers = root
            .Providers.Select(provider => (provider, label(provider)))
            .ToList();
        List<string> keys = root.AsEnumerable()
            .Where(pair => pair.Value != null)
            .Select(pair => pair.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var layers = providers
            .Select(
                (entry, index) =>
                    new ConfigurationLayer(
                        index + 1,
                        entry.Source.Layer,
                        entry.Source.Source,
                        entry.Source.Present,
                        keys.Count(key =>
                            entry.Provider.TryGet(key, out string value) && value != null
                        )
                    )
            )
            .ToList();

        string prefix = string.IsNullOrWhiteSpace(section) ? null : section.Trim().TrimEnd(':');
        var settings = new List<EffectiveSetting>();
        foreach (
            string key in keys.Where(key => prefix == null || InSection(key, prefix))
                .Order(StringComparer.OrdinalIgnoreCase)
        )
        {
            var setters = providers
                .Where(entry => entry.Provider.TryGet(key, out string value) && value != null)
                .ToList();
            if (setters.Count == 0)
            {
                continue;
            }

            (IConfigurationProvider winner, ConfigurationLayerSource source) = setters[^1];
            winner.TryGet(key, out string raw);
            bool fromSecrets = setters.Any(entry =>
                string.Equals(
                    entry.Source.Layer,
                    ConfigurationLayers.Secrets,
                    StringComparison.Ordinal
                )
            );
            string shown = ConfigurationRedaction.Show(key, raw, fromSecrets, out bool redacted);
            settings.Add(
                new EffectiveSetting(
                    key,
                    shown,
                    redacted,
                    source.Layer,
                    source.Source,
                    setters
                        .Take(setters.Count - 1)
                        .Select(entry => entry.Source.Source)
                        .Reverse()
                        .ToList()
                )
            );
        }

        bool found = prefix == null || settings.Count > 0;
        return new EffectiveConfiguration
        {
            Ok = found,
            Code = found ? null : SectionNotFound,
            Message = found
                ? settings.Count
                    + " keys, "
                    + settings.Count(setting => setting.Redacted)
                    + " redacted."
                : "No key is set at or under '" + prefix + "'.",
            Section = prefix,
            Layers = layers,
            RedactedCount = settings.Count(setting => setting.Redacted),
            Settings = settings,
        };
    }

    private static bool InSection(string key, string section) =>
        string.Equals(key, section, StringComparison.OrdinalIgnoreCase)
        || key.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// What <c>config effective</c> never prints. A key is redacted when any layer that sets it is
/// <c>appsettings.secrets.json</c>, or when its last name segment (an array index is skipped)
/// contains Key, Token, Secret, Password, Credential, or ConnectionString, whichever layer set
/// it. A redacted value shows as <c>(set)</c> or <c>(empty)</c>. Any <c>op://</c> value shows as
/// <c>(op:// reference)</c> and is never resolved. Every other value still goes through the
/// role-log token rules (<see cref="ServiceLogRedaction"/>). There is no switch that shows them.
/// </summary>
public static class ConfigurationRedaction
{
    public const string Set = "(set)";
    public const string Empty = "(empty)";
    public const string Reference = "(op:// reference)";

    private static readonly Regex SecretName = new(
        "Key|Token|Secret|Password|Credential|ConnectionString",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    /// <summary>True when the key's name marks a secret, whatever layer set it.</summary>
    public static bool IsSecretName(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        string name = key.Split(':')
            .Reverse()
            .FirstOrDefault(segment => segment.Length > 0 && !segment.All(char.IsDigit));
        return name != null && SecretName.IsMatch(name);
    }

    /// <summary>The value as <c>config effective</c> prints it.</summary>
    /// <param name="fromSecrets">A layer that sets the key is <c>appsettings.secrets.json</c>.</param>
    public static string Show(string key, string value, bool fromSecrets, out bool redacted)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.StartsWith("op://", StringComparison.OrdinalIgnoreCase))
        {
            redacted = true;
            return Reference;
        }

        if (fromSecrets || IsSecretName(key))
        {
            redacted = true;
            return trimmed.Length == 0 ? Empty : Set;
        }

        string scrubbed = ServiceLogRedaction.Redact(value);
        redacted = !string.Equals(scrubbed, value, StringComparison.Ordinal);
        return scrubbed;
    }
}
