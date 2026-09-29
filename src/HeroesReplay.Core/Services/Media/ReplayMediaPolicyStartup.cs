using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace HeroesReplay.Core.Services.Media;

/// <summary>
/// Binds replay media policy settings. Invalid values fail closed:
/// startup refuses them, and <see cref="ReplayMediaPolicy.Evaluate"/> records nothing.
/// </summary>
public static class ReplayMediaPolicyStartup
{
    public const string SectionName = "ReplayMedia";
    public const string Unreadable = "configuration-unreadable";

    public static ReplayMediaPolicySettings Require(IConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        ReplayMediaPolicySettings settings = Bind(configuration, out IReadOnlyList<string> errors);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Replay media policy configuration is invalid: " + string.Join(", ", errors)
            );
        }

        return settings;
    }

    public static ReplayMediaPolicySettings Bind(
        IConfiguration configuration,
        out IReadOnlyList<string> errors
    )
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var found = new List<string>();
        var settings = new ReplayMediaPolicySettings();
        IConfigurationSection section = configuration.GetSection(SectionName);
        Apply(section, settings, found);
        foreach (string error in ReplayMediaPolicy.Validate(settings))
        {
            found.Add(error);
        }

        errors = found.Count == 0 ? Array.Empty<string>() : found.ToArray();
        return settings;
    }

    private static void Apply(
        IConfigurationSection section,
        ReplayMediaPolicySettings settings,
        List<string> errors
    )
    {
        if (Present(section, nameof(ReplayMediaPolicySettings.Version)))
        {
            settings.Version = section[nameof(ReplayMediaPolicySettings.Version)].Trim();
        }

        if (Present(section, nameof(ReplayMediaPolicySettings.RecordingMode)))
        {
            if (
                !TryNamedEnum(
                    section[nameof(ReplayMediaPolicySettings.RecordingMode)],
                    out ReplayRecordingMode recording
                )
            )
            {
                settings.RecordingMode = (ReplayRecordingMode)(-1);
            }
            else
            {
                settings.RecordingMode = recording;
            }
        }

        if (Present(section, nameof(ReplayMediaPolicySettings.PublicationMode)))
        {
            if (
                !TryNamedEnum(
                    section[nameof(ReplayMediaPolicySettings.PublicationMode)],
                    out ReplayPublicationMode publication
                )
            )
            {
                settings.PublicationMode = (ReplayPublicationMode)(-1);
            }
            else
            {
                settings.PublicationMode = publication;
            }
        }

        ApplyBool(
            section,
            nameof(ReplayMediaPolicySettings.RequireCurrentPatch),
            value => settings.RequireCurrentPatch = value,
            errors
        );
        ApplyBool(
            section,
            nameof(ReplayMediaPolicySettings.RequestsBypassPatchRequirement),
            value => settings.RequestsBypassPatchRequirement = value,
            errors
        );

        if (Present(section, nameof(ReplayMediaPolicySettings.MinimumGameVersion)))
        {
            settings.MinimumGameVersion = section[
                nameof(ReplayMediaPolicySettings.MinimumGameVersion)
            ];
        }

        if (Present(section, nameof(ReplayMediaPolicySettings.MinimumHighSkillRank)))
        {
            settings.MinimumHighSkillRank = section[
                nameof(ReplayMediaPolicySettings.MinimumHighSkillRank)
            ];
        }

        if (Present(section, nameof(ReplayMediaPolicySettings.MinimumHighSkillMmr)))
        {
            string text = section[nameof(ReplayMediaPolicySettings.MinimumHighSkillMmr)];
            if (string.IsNullOrWhiteSpace(text))
            {
                settings.MinimumHighSkillMmr = null;
            }
            else if (
                !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mmr)
            )
            {
                errors.Add(
                    Unreadable + ":" + nameof(ReplayMediaPolicySettings.MinimumHighSkillMmr)
                );
            }
            else
            {
                settings.MinimumHighSkillMmr = mmr;
            }
        }

        ApplyAge(
            section,
            nameof(ReplayMediaPolicySettings.OrdinaryCandidateMaxAge),
            value => settings.OrdinaryCandidateMaxAge = value,
            errors
        );
        ApplyAge(
            section,
            nameof(ReplayMediaPolicySettings.HighSkillCandidateMaxAge),
            value => settings.HighSkillCandidateMaxAge = value,
            errors
        );
        ApplyAge(
            section,
            nameof(ReplayMediaPolicySettings.NotableCandidateMaxAge),
            value => settings.NotableCandidateMaxAge = value,
            errors
        );
        ApplyAge(
            section,
            nameof(ReplayMediaPolicySettings.RequestedCandidateMaxAge),
            value => settings.RequestedCandidateMaxAge = value,
            errors
        );
    }

    private static void ApplyBool(
        IConfigurationSection section,
        string key,
        Action<bool> assign,
        List<string> errors
    )
    {
        if (!Present(section, key))
        {
            return;
        }

        if (!bool.TryParse(section[key], out bool value))
        {
            errors.Add(Unreadable + ":" + key);
            return;
        }

        assign(value);
    }

    private static void ApplyAge(
        IConfigurationSection section,
        string key,
        Action<TimeSpan> assign,
        List<string> errors
    )
    {
        if (!Present(section, key))
        {
            return;
        }

        if (!TimeSpan.TryParse(section[key], CultureInfo.InvariantCulture, out TimeSpan value))
        {
            errors.Add(Unreadable + ":" + key);
            return;
        }

        assign(value);
    }

    private static bool Present(IConfiguration section, string key)
    {
        return section?[key] != null;
    }

    private static bool TryNamedEnum<TEnum>(string text, out TEnum value)
        where TEnum : struct
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        if (!Enum.TryParse(trimmed, ignoreCase: false, out value))
        {
            return false;
        }

        return Enum.IsDefined(typeof(TEnum), value)
            && string.Equals(trimmed, value.ToString(), StringComparison.Ordinal);
    }
}
