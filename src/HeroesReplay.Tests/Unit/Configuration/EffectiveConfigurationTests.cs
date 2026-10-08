using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeroesReplay.CLI.Commands.Config;
using HeroesReplay.Core.Configuration;
using Xunit;

namespace HeroesReplay.Tests.Unit.Configuration;

/// <summary>
/// <c>config effective</c> (#312): the layer that won each key, the overlay order, and secrets
/// that never reach the output. Each test uses its own folder and a section name of its own, so
/// the <c>HEROES_REPLAY_</c> variables it sets cannot change another test's keys.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class EffectiveConfigurationTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-config-" + Path.GetRandomFileName()
    );
    private readonly string probe = "CfgProbe" + Guid.NewGuid().ToString("N")[..12];
    private readonly List<string> variables = new();

    public EffectiveConfigurationTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        foreach (string variable in variables)
        {
            Environment.SetEnvironmentVariable(variable, null);
        }

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Provenance_AVariableOverridesProd_AndProdOverridesBase()
    {
        WriteJson(
            "appsettings.json",
            new
            {
                Value = "base",
                BaseOnly = "b",
                Overlaid = "base",
            }
        );
        WriteJson("appsettings.prod.json", new { Value = "prod", Overlaid = "prod" });
        SetVariable("Value", "variable");

        JsonElement json = EffectiveJson(environment: "prod", section: probe);

        Assert.True(json.GetProperty("ok").GetBoolean());
        Assert.Equal("prod", json.GetProperty("environment").GetString());
        Assert.Equal("--environment", json.GetProperty("environmentSource").GetString());
        Assert.Equal(
            ["base", "secrets", "overlay", "environment"],
            json.GetProperty("layers")
                .EnumerateArray()
                .Select(layer => layer.GetProperty("layer").GetString())
        );
        Assert.Equal(
            ["appsettings.json", "appsettings.secrets.json", "appsettings.prod.json"],
            json.GetProperty("layers")
                .EnumerateArray()
                .Take(3)
                .Select(layer => layer.GetProperty("source").GetString())
        );
        Assert.False(json.GetProperty("layers")[1].GetProperty("present").GetBoolean());

        JsonElement value = Setting(json, probe + ":Value");
        Assert.Equal("variable", value.GetProperty("value").GetString());
        Assert.Equal("environment", value.GetProperty("layer").GetString());
        Assert.Equal(
            ConfigCommand.EnvironmentVariablesSource,
            value.GetProperty("source").GetString()
        );
        Assert.Equal(
            ["appsettings.prod.json", "appsettings.json"],
            value.GetProperty("overrides").EnumerateArray().Select(source => source.GetString())
        );

        JsonElement overlaid = Setting(json, probe + ":Overlaid");
        Assert.Equal("prod", overlaid.GetProperty("value").GetString());
        Assert.Equal("overlay", overlaid.GetProperty("layer").GetString());
        Assert.Equal(
            ["appsettings.json"],
            overlaid.GetProperty("overrides").EnumerateArray().Select(source => source.GetString())
        );

        JsonElement baseOnly = Setting(json, probe + ":BaseOnly");
        Assert.Equal("base", baseOnly.GetProperty("layer").GetString());
        Assert.Empty(baseOnly.GetProperty("overrides").EnumerateArray());
        Assert.False(baseOnly.GetProperty("redacted").GetBoolean());
    }

    [Fact]
    public void Provenance_AnOverlayWithoutAFile_LeavesBaseInPlace()
    {
        WriteJson("appsettings.json", new { Value = "base" });
        WriteJson("appsettings.prod.json", new { Value = "prod" });

        JsonElement json = EffectiveJson(environment: "dev", section: probe);

        JsonElement overlay = json.GetProperty("layers")[2];
        Assert.Equal("overlay", overlay.GetProperty("layer").GetString());
        Assert.Equal("appsettings.dev.json", overlay.GetProperty("source").GetString());
        Assert.False(overlay.GetProperty("present").GetBoolean());
        JsonElement value = Setting(json, probe + ":Value");
        Assert.Equal("base", value.GetProperty("value").GetString());
        Assert.Equal("appsettings.json", value.GetProperty("source").GetString());
    }

    [Fact]
    public void Secrets_EveryExamplePathAndAVariableToken_AreRedactedInTextAndJson()
    {
        // Every path in appsettings.secrets.example.json, with a literal value where it is empty.
        JsonObject secrets = JsonNode
            .Parse(File.ReadAllText(Path.Combine(CliFolder(), "appsettings.secrets.example.json")))
            .AsObject();
        var literals = new List<string>();
        var paths = new List<string>();
        foreach ((string section, JsonNode node) in secrets.ToList())
        {
            foreach ((string name, JsonNode value) in node.AsObject().ToList())
            {
                paths.Add(section + ":" + name);
                if (string.IsNullOrEmpty((string)value))
                {
                    string literal = "lit" + Guid.NewGuid().ToString("N")[..10] + name;
                    literals.Add(literal);
                    node[name] = literal;
                }
                else
                {
                    // The op:// references: the reference text never reaches the output either.
                    literals.Add((string)value);
                }
            }
        }

        secrets[probe] = new JsonObject { ["Plain"] = "secretfile-plain-value" };
        File.WriteAllText(Path.Combine(root, "appsettings.secrets.json"), secrets.ToJsonString());
        WriteJson("appsettings.json", new { Shown = "visible-value" });
        const string token = "tok_live_9f8e7d6c5b4a";
        SetVariable("AccessToken", token);
        // A secrets-file key stays redacted when a variable overrides it.
        SetVariable("Plain", "variable-plain-value");

        var text = new StringWriter();
        var json = new StringWriter();
        Assert.Equal(0, ConfigCommand.Effective(root, "prod", null, json: false, text));
        Assert.Equal(0, ConfigCommand.Effective(root, "prod", null, json: true, json));

        foreach (string output in new[] { text.ToString(), json.ToString() })
        {
            foreach (string literal in literals)
            {
                Assert.DoesNotContain(literal, output, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("op://Heroes", output, StringComparison.Ordinal);
            Assert.DoesNotContain(token, output, StringComparison.Ordinal);
            Assert.DoesNotContain("plain-value", output, StringComparison.Ordinal);
            Assert.Contains("visible-value", output, StringComparison.Ordinal);
        }

        using JsonDocument document = JsonDocument.Parse(json.ToString());
        foreach (string path in paths.Append(probe + ":AccessToken").Append(probe + ":Plain"))
        {
            JsonElement setting = Setting(document.RootElement, path);
            Assert.True(setting.GetProperty("redacted").GetBoolean(), path);
            Assert.Contains(
                setting.GetProperty("value").GetString(),
                new[]
                {
                    ConfigurationRedaction.Set,
                    ConfigurationRedaction.Empty,
                    ConfigurationRedaction.Reference,
                }
            );
        }

        Assert.Equal(
            ConfigurationRedaction.Reference,
            Setting(document.RootElement, "HeroesProfileApi:ApiKey")
                .GetProperty("value")
                .GetString()
        );
        Assert.Contains(
            "Twitch:AccessToken = (set)  [secrets: appsettings.secrets.json] redacted",
            text.ToString(),
            StringComparison.Ordinal
        );
        Assert.True(
            document.RootElement.GetProperty("redactedCount").GetInt32() >= paths.Count + 2
        );
    }

    [Fact]
    public void Envelope_HasTheSchemaVersionOkAndCode()
    {
        var missing = new StringWriter();
        Assert.Equal(1, ConfigCommand.Effective(root, "prod", null, json: true, missing));
        using (JsonDocument document = JsonDocument.Parse(missing.ToString()))
        {
            Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                ConfigurationProvenance.BaseMissing,
                document.RootElement.GetProperty("code").GetString()
            );
        }

        WriteJson("appsettings.json", new { Value = "base" });
        var found = new StringWriter();
        var notFound = new StringWriter();
        Assert.Equal(0, ConfigCommand.Effective(root, null, probe, json: true, found));
        Assert.Equal(1, ConfigCommand.Effective(root, null, probe + "Nope", json: true, notFound));
        using (JsonDocument document = JsonDocument.Parse(found.ToString()))
        {
            Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("code").ValueKind);
            Assert.Equal(probe, document.RootElement.GetProperty("section").GetString());
            Assert.Single(document.RootElement.GetProperty("settings").EnumerateArray());
        }

        using (JsonDocument document = JsonDocument.Parse(notFound.ToString()))
        {
            Assert.Equal(
                ConfigurationProvenance.SectionNotFound,
                document.RootElement.GetProperty("code").GetString()
            );
        }

        File.WriteAllText(Path.Combine(root, "appsettings.json"), "{ \"Broken\": ");
        var unreadable = new StringWriter();
        Assert.Equal(1, ConfigCommand.Effective(root, null, null, json: false, unreadable));
        Assert.Contains(ConfigurationProvenance.Unreadable, unreadable.ToString());
    }

    [Theory]
    [InlineData("Twitch:AccessToken", true)]
    [InlineData("Twitch:RefreshToken", true)]
    [InlineData("HeroesProfileApi:ApiKey", true)]
    [InlineData("OBS:WebSocketPassword", true)]
    [InlineData("Github:AccessToken", true)]
    [InlineData("Database:ConnectionString", true)]
    [InlineData("YouTube:ClientSecret", true)]
    [InlineData("Service:Credentials:0", true)]
    [InlineData("Twitch:Channel", false)]
    [InlineData("OBS:StreamingEnabled", false)]
    [InlineData("Release:Enabled", false)]
    [InlineData("OBS:RankImagesSourceNames:0", false)]
    public void SecretNames_AreMatchedOnTheLastNamedSegment(string key, bool secret)
    {
        Assert.Equal(secret, ConfigurationRedaction.IsSecretName(key));
    }

    [Fact]
    public void Show_HidesReferencesAndTokenLikeText_AndKeepsPlainValues()
    {
        Assert.Equal(
            ConfigurationRedaction.Reference,
            ConfigurationRedaction.Show(
                "Any:Value",
                "op://Vault/Item/field",
                false,
                out bool reference
            )
        );
        Assert.True(reference);
        Assert.Equal(
            ConfigurationRedaction.Empty,
            ConfigurationRedaction.Show("OBS:WebSocketPassword", "", false, out bool empty)
        );
        Assert.True(empty);
        Assert.Equal(
            ConfigurationRedaction.Set,
            ConfigurationRedaction.Show("Twitch:Channel", "saltysadism", true, out bool fromSecrets)
        );
        Assert.True(fromSecrets);

        string url = ConfigurationRedaction.Show(
            "Report:Url",
            "https://example.test/page?token=abc123def",
            false,
            out bool scrubbed
        );
        Assert.True(scrubbed);
        Assert.DoesNotContain("abc123def", url, StringComparison.Ordinal);

        Assert.Equal(
            "true",
            ConfigurationRedaction.Show("OBS:StreamingEnabled", "true", false, out bool plain)
        );
        Assert.False(plain);
    }

    private JsonElement EffectiveJson(string environment, string section)
    {
        var output = new StringWriter();
        int exit = ConfigCommand.Effective(root, environment, section, json: true, output);
        Assert.True(exit == 0, output.ToString());
        return JsonDocument.Parse(output.ToString()).RootElement.Clone();
    }

    private static JsonElement Setting(JsonElement json, string key) =>
        json.GetProperty("settings")
            .EnumerateArray()
            .Single(setting =>
                string.Equals(
                    setting.GetProperty("key").GetString(),
                    key,
                    StringComparison.OrdinalIgnoreCase
                )
            );

    private void WriteJson(string file, object values) =>
        File.WriteAllText(
            Path.Combine(root, file),
            JsonSerializer.Serialize(new Dictionary<string, object> { [probe] = values })
        );

    private void SetVariable(string name, string value)
    {
        string variable = "HEROES_REPLAY_" + probe + "__" + name;
        variables.Add(variable);
        Environment.SetEnvironmentVariable(variable, value);
    }

    private static string CliFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "heroes-replay.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(
            dir?.FullName ?? throw new FileNotFoundException("heroes-replay.slnx"),
            "src",
            "HeroesReplay.CLI"
        );
    }
}
