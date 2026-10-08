using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.ServiceHost.Logs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace HeroesReplay.CLI.Commands.Config;

/// <summary>
/// <c>config effective</c>: every effective setting, the layer that set it, and the overlay
/// order, with every secret redacted (<see cref="ConfigurationRedaction"/>). Read-only. It
/// builds the configuration the way the roles do (<see cref="ServiceCollectionExtensions.BuildConfiguration"/>)
/// and resolves no <c>op://</c> reference.
/// </summary>
public class ConfigCommand : Command
{
    public const string EnvironmentVariablesSource = "HEROES_REPLAY_ environment variables";

    private const int TextValueLimit = 160;

    public ConfigCommand()
        : base(
            "config",
            "This install's configuration as its roles see it. Read-only; never prints a secret value."
        )
    {
        Subcommands.Add(EffectiveCommand());
    }

    private static Command EffectiveCommand()
    {
        var command = new Command(
            "effective",
            "Print every effective setting with the layer that set it (base appsettings.json, appsettings.secrets.json, the appsettings.{env}.json overlay, then HEROES_REPLAY_ variables; a later layer wins), the layers it overrides, the overlay order, and the environment name. Secrets are always redacted, with no switch to show them: every key appsettings.secrets.json sets and every key whose name contains Key, Token, Secret, Password, Credential, or ConnectionString shows as (set) or (empty), and an op:// value as (op:// reference), never resolved. Variables set only in start-live.cmd apply to the processes it starts, not to this shell. Exit 1 when appsettings.json is missing or unreadable, or --section matches nothing."
        );
        Option<string> section = new("--section")
        {
            Description =
                "Only keys at or under this section, for example OBS or Twitch:Predictions.",
        };
        Option<string> environment = new("--environment")
        {
            Description =
                "The appsettings overlay to apply (dev, prod). Default: HEROES_REPLAY_ENV.",
        };
        Option<string> install = new("--install")
        {
            Description =
                "The folder with appsettings.json. Default: the current directory when it has one, otherwise this exe's folder.",
        };
        Option<bool> redact = new("--redact")
        {
            Description =
                "Redaction is always on; this flag changes nothing and is accepted for the #130 spelling.",
        };
        Option<string> format = CliOutput.CreateOption(
            "JSON: schemaVersion, ok, code, message, environment, environmentSource, basePath, section, layers, redactedCount, settings."
        );
        command.Options.Add(section);
        command.Options.Add(environment);
        command.Options.Add(install);
        command.Options.Add(redact);
        command.Options.Add(format);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(
                    Effective(
                        parseResult.GetValue(install),
                        parseResult.GetValue(environment),
                        parseResult.GetValue(section),
                        CliOutput.Format(parseResult, format) == CliOutputFormat.Json,
                        CliOutput.Out(parseResult)
                    )
                );
            }
        );
        return command;
    }

    /// <summary>Writes the effective configuration and returns the exit code.</summary>
    public static int Effective(
        string install,
        string environment,
        string section,
        bool json,
        TextWriter output
    )
    {
        string basePath = string.IsNullOrWhiteSpace(install)
            ? ServiceCollectionExtensions.DefaultBasePath()
            : Path.GetFullPath(install);
        (string name, string from) = EnvironmentName(environment);
        EffectiveConfiguration effective = Read(basePath, name, section) with
        {
            Environment = name,
            EnvironmentSource = from,
            BasePath = basePath,
        };

        if (json)
        {
            return CliOutput.WriteJson(effective, output);
        }

        WriteText(effective, output);
        return CliOutput.ExitCode(effective);
    }

    /// <summary>The provider's layer, as <see cref="ServiceCollectionExtensions.BuildConfiguration"/> adds them.</summary>
    internal static ConfigurationLayerSource Label(IConfigurationProvider provider, string basePath)
    {
        switch (provider)
        {
            case JsonConfigurationProvider file:
                string path = file.Source.Path;
                string layer =
                    string.Equals(path, "appsettings.json", StringComparison.OrdinalIgnoreCase)
                        ? ConfigurationLayers.Base
                    : string.Equals(
                        path,
                        "appsettings.secrets.json",
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? ConfigurationLayers.Secrets
                    : ConfigurationLayers.Overlay;
                return new ConfigurationLayerSource(
                    layer,
                    path,
                    File.Exists(Path.Combine(basePath, path))
                );
            case EnvironmentVariablesConfigurationProvider:
                return new ConfigurationLayerSource(
                    ConfigurationLayers.Environment,
                    EnvironmentVariablesSource,
                    true
                );
            default:
                return new ConfigurationLayerSource("other", provider.GetType().Name, true);
        }
    }

    private static EffectiveConfiguration Read(string basePath, string environment, string section)
    {
        if (!File.Exists(Path.Combine(basePath, "appsettings.json")))
        {
            return new EffectiveConfiguration
            {
                Ok = false,
                Code = ConfigurationProvenance.BaseMissing,
                Message = "No appsettings.json in " + basePath + ". Pass --install <folder>.",
            };
        }

        IConfigurationRoot root;
        try
        {
            root = ServiceCollectionExtensions.BuildConfiguration(basePath, environment);
        }
        catch (Exception e)
            when (e
                    is InvalidDataException
                        or FormatException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            // The parser names the file and line, never the value.
            return new EffectiveConfiguration
            {
                Ok = false,
                Code = ConfigurationProvenance.Unreadable,
                Message = ServiceLogRedaction.Redact(e.Message),
            };
        }

        return ConfigurationProvenance.Describe(
            root,
            provider => Label(provider, basePath),
            section
        );
    }

    private static (string Name, string From) EnvironmentName(string option)
    {
        if (!string.IsNullOrWhiteSpace(option))
        {
            return (option.Trim(), "--environment");
        }

        string variable = Environment.GetEnvironmentVariable(
            ServiceCollectionExtensions.EnvironmentVariable
        );
        return string.IsNullOrWhiteSpace(variable)
            ? (null, "none")
            : (variable.Trim(), ServiceCollectionExtensions.EnvironmentVariable);
    }

    private static void WriteText(EffectiveConfiguration effective, TextWriter output)
    {
        output.WriteLine(
            $"Effective configuration in {effective.BasePath}, environment {effective.Environment ?? "(none)"} (from {effective.EnvironmentSource})."
        );
        if (effective.Layers.Count > 0)
        {
            output.WriteLine("Layers, lowest first; a later layer wins:");
            foreach (ConfigurationLayer layer in effective.Layers)
            {
                output.WriteLine(
                    $"  {layer.Order}. {layer.Layer, -11} {layer.Source, -37} {(layer.Present ? layer.Keys + (layer.Keys == 1 ? " key" : " keys") : "missing")}"
                );
            }

            output.WriteLine(
                $"Secrets are redacted: {ConfigurationRedaction.Set}, {ConfigurationRedaction.Empty}, {ConfigurationRedaction.Reference}."
            );
        }

        foreach (EffectiveSetting setting in effective.Settings)
        {
            string value =
                setting.Value.Length > TextValueLimit
                    ? setting.Value[..TextValueLimit] + $"... ({setting.Value.Length} chars)"
                    : setting.Value;
            string overrides =
                setting.Overrides.Count == 0
                    ? string.Empty
                    : "; overrides " + string.Join(", ", setting.Overrides);
            output.WriteLine(
                $"{setting.Key} = {value}  [{setting.Layer}: {setting.Source}{overrides}]{(setting.Redacted ? " redacted" : string.Empty)}"
            );
        }

        output.WriteLine(
            effective.Ok ? effective.Message : $"{effective.Code}: {effective.Message}"
        );
    }
}
