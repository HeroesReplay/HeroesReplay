using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using HeroesReplay.CLI.Commands;
using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.CLI.Commands.Client;
using HeroesReplay.CLI.Commands.Deps;
using HeroesReplay.CLI.Commands.Obs;
using HeroesReplay.Core.Dependencies;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Inspection;
using Xunit;

namespace HeroesReplay.Tests.Unit.Support;

/// <summary>
/// Keeps the agent docs in step with the code: every CLI command is in the
/// <c>heroes-replay-cli</c> skill, and every folder under <c>.agents/skills</c> is a first-party
/// skill named in <c>AGENTS.md</c> or a vendored one pinned in <c>vendored.json</c>.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class AgentDocsTests
{
    private static readonly string Root = RepoRoot();
    private static readonly string Skills = Path.Combine(Root, ".agents", "skills");

    [Fact]
    public void CliSkill_NamesEveryCommand()
    {
        string skill = File.ReadAllText(Path.Combine(Skills, "heroes-replay-cli", "SKILL.md"));

        List<string> missing = CommandPaths(new HeroesReplayCommand(), null)
            .Where(path => !skill.Contains("`" + path, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "The heroes-replay-cli skill does not name: " + string.Join(", ", missing)
        );
    }

    /// <summary>The JSON output contract (#311): every stable code a converted command prints.</summary>
    [Fact]
    public void CliSkill_NamesEveryJsonOutputCode()
    {
        string skill = File.ReadAllText(Path.Combine(Skills, "heroes-replay-cli", "SKILL.md"));
        string[] codes =
        [
            .. CheckCodes.All,
            ObsCommand.IngestReady,
            ObsCommand.StreamingDisabled,
            ObsStreamArm.NotArmedReason,
            ObsLiveRead.SettingsUnreadable,
            ClientCommand.PresetOk,
            ClientCommand.PresetMismatch,
            ClientCommand.StatusError,
            DepsCommand.Installed,
            DepsCommand.AlreadyInstalled,
            DepsCommand.Failed,
        ];

        List<string> missing = codes
            .Where(code => !skill.Contains("`" + code + "`", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "The heroes-replay-cli skill does not document: " + string.Join(", ", missing)
        );
        Assert.Contains("## JSON output contract", skill, StringComparison.Ordinal);
    }

    /// <summary>The command reference (#313 F9) has one entry per command that runs, and no other.</summary>
    [Fact]
    public void CommandReference_HasAnEntryForEveryCommandThatRuns()
    {
        List<string> commands = CommandReference
            .Runnable(new HeroesReplayCommand())
            .Select(command => command.Path)
            .ToList();
        List<string> entries = CommandReference.Facts.Select(fact => fact.Path).ToList();

        Assert.Equal(entries.Count, entries.Distinct(StringComparer.Ordinal).Count());
        List<string> missing = commands.Except(entries, StringComparer.Ordinal).ToList();
        List<string> stale = entries.Except(commands, StringComparer.Ordinal).ToList();
        Assert.True(
            missing.Count == 0,
            "CommandReference.Facts has no entry for: " + string.Join(", ", missing)
        );
        Assert.True(
            stale.Count == 0,
            "CommandReference.Facts names commands that do not exist: " + string.Join(", ", stale)
        );
        Assert.All(
            CommandReference.Facts,
            fact =>
            {
                Assert.False(string.IsNullOrWhiteSpace(fact.Before), fact.Path + " has no Before.");
                Assert.False(
                    string.IsNullOrWhiteSpace(fact.Changes),
                    fact.Path + " has no Changes."
                );
                Assert.False(string.IsNullOrWhiteSpace(fact.Exit), fact.Path + " has no Exit.");
            }
        );
    }

    /// <summary>
    /// Every string constant in the CLI and Core that looks like a stable code
    /// (<c>area.reason</c> or <c>check.target.reason</c>) is listed under some command.
    /// </summary>
    [Fact]
    public void CommandReference_NamesEveryStableCode()
    {
        var code = new Regex(
            @"^(check|client|config|deps|download|obs|service|spectate|twitch|youtube)\.[a-z0-9_]+(\.[a-z0-9_]+)?$"
        );
        IEnumerable<string> constants = new[]
        {
            typeof(HeroesReplayCommand).Assembly,
            typeof(ObsValidator).Assembly,
        }
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type =>
                type.GetFields(
                    System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Static
                )
            )
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue())
            .Where(value => value != null && code.IsMatch(value))
            .Distinct(StringComparer.Ordinal);
        var listed = CommandReference
            .Facts.SelectMany(fact => fact.Codes ?? [])
            .ToHashSet(StringComparer.Ordinal);

        List<string> missing = constants.Where(value => !listed.Contains(value)).Order().ToList();

        Assert.True(
            missing.Count == 0,
            "These stable codes are in no CommandReference.Facts entry: "
                + string.Join(", ", missing)
        );
    }

    /// <summary>
    /// The checked-in reference is what <see cref="CommandReference.Render"/> makes from the code.
    /// Set HEROESREPLAY_WRITE_COMMAND_REFERENCE=1 to rewrite it.
    /// </summary>
    [Fact]
    public void CommandReference_IsCurrent()
    {
        string path = Path.Combine(Root, CommandReference.RelativePath);
        string expected = CommandReference.Render(new HeroesReplayCommand());
        if (Environment.GetEnvironmentVariable(CommandReference.WriteVariable) == "1")
        {
            File.WriteAllText(path, expected);
        }

        Assert.True(File.Exists(path), path + " is missing.");
        string actual = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            CommandReference.RelativePath
                + " is stale. Run `dotnet test heroes-replay.slnx --filter CommandReference` with "
                + CommandReference.WriteVariable
                + "=1 and commit the file."
        );
        string skill = File.ReadAllText(Path.Combine(Skills, "heroes-replay-cli", "SKILL.md"));
        Assert.Contains("commands.md", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// G2 (#313): Grok 1.0.46 scans <c>.agents/skills</c> itself, beside <c>.grok/skills</c>, so
    /// there is no mirror. A copy under <c>.grok</c> would shadow the real skill when it goes stale.
    /// </summary>
    [Fact]
    public void Grok_ReadsAgentsSkills_SoThereIsNoGrokMirror()
    {
        Assert.False(
            Directory.Exists(Path.Combine(Root, ".grok", "skills")),
            ".grok/skills exists. Grok reads .agents/skills directly; a mirror goes stale and shadows it."
        );
        Assert.False(
            Directory.Exists(Path.Combine(Root, ".grok", "commands")),
            ".grok/commands exists. Put skills in .agents/skills."
        );
        Assert.True(
            File.Exists(Path.Combine(Root, ".grok", "config.toml")),
            ".grok/config.toml (the MCP server) is missing."
        );
        Assert.Contains(
            "Grok reads `.agents/skills` itself",
            File.ReadAllText(Path.Combine(Root, "AGENTS.md")),
            StringComparison.Ordinal
        );
    }

    /// <summary>
    /// Names Grok and Claude Code both accept (lowercase letters, digits, and hyphens, 2 to 64
    /// characters), and a SKILL.md under Grok's 25,000-token inline cap (about 100 KB).
    /// </summary>
    [Fact]
    public void Skills_HaveNamesAndSizesGrokAccepts()
    {
        var name = new Regex("^[a-z0-9][a-z0-9-]{0,62}[a-z0-9]$");
        foreach (string folder in Directory.GetDirectories(Skills))
        {
            Assert.Matches(name, Path.GetFileName(folder));
            long size = new FileInfo(Path.Combine(folder, "SKILL.md")).Length;
            Assert.True(size <= 100_000, folder + "/SKILL.md is " + size + " bytes.");
        }
    }

    /// <summary>G4/G5 (#313): the OBS guidance is split into the generic reference, the safety rules, and the code.</summary>
    [Fact]
    public void ObsSkills_AreSplitAndPointAtEachOther()
    {
        string docs = File.ReadAllText(Path.Combine(Skills, "obs-docs", "SKILL.md"));
        string operations = File.ReadAllText(Path.Combine(Skills, "heroes-replay-obs", "SKILL.md"));
        string code = File.ReadAllText(Path.Combine(Skills, "obs-websocket-v5", "SKILL.md"));

        Assert.Contains("docs/obs-operations.md", operations, StringComparison.Ordinal);
        Assert.Contains("obs-docs", operations, StringComparison.Ordinal);
        Assert.Contains("obs-websocket-v5", operations, StringComparison.Ordinal);
        Assert.Contains("heroes-replay-obs", docs, StringComparison.Ordinal);
        Assert.Contains("heroes-replay-obs", code, StringComparison.Ordinal);
        Assert.Contains("obs-docs", code, StringComparison.Ordinal);
        // Nothing third-party is copied in: media-os was evaluated, not vendored.
        Assert.Contains("media-os", docs, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "media-os",
            File.ReadAllText(Path.Combine(Skills, "vendored.json")),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void FfmpegDocs_PointToThePinnedDefinition()
    {
        const string pin = "src/HeroesReplay.Core/Dependencies/dependencies.json";
        Assert.True(File.Exists(Path.Combine(Root, pin)), pin + " is missing.");
        string version = DependencyManifest.Ffmpeg.Version;
        foreach (
            string doc in new[]
            {
                "AGENTS.md",
                Path.Combine(".agents", "skills", "ffmpeg", "SKILL.md"),
                Path.Combine(".agents", "skills", "heroes-replay-cli", "SKILL.md"),
            }
        )
        {
            string text = File.ReadAllText(Path.Combine(Root, doc));
            Assert.True(
                text.Contains(pin, StringComparison.Ordinal),
                doc + " does not name " + pin
            );
            Assert.True(
                text.Contains(version, StringComparison.Ordinal),
                doc + " does not name the pinned ffmpeg " + version
            );
        }
    }

    [Fact]
    public void Skills_HaveTheirFolderNameAndADescription()
    {
        foreach (string folder in Directory.GetDirectories(Skills))
        {
            string file = Path.Combine(folder, "SKILL.md");
            Assert.True(File.Exists(file), folder + " has no SKILL.md.");
            string text = File.ReadAllText(file);
            Match name = Regex.Match(text, @"^name:\s*(\S+)\s*$", RegexOptions.Multiline);
            Assert.True(name.Success, file + " has no name.");
            Assert.Equal(Path.GetFileName(folder), name.Groups[1].Value);
            Assert.Matches(new Regex(@"^description:\s*\S", RegexOptions.Multiline), text);
        }
    }

    /// <summary>
    /// A Windows path written through an escaping layer loses its backslashes: <c>C:\heroesreplay\app</c>
    /// once landed in the CLI skill as <c>C:heroesreplay</c>, a BEL character, and <c>pp</c>. No doc
    /// keeps a control character or a drive letter that is not followed by a separator.
    /// </summary>
    [Fact]
    public void Docs_KeepTheirWindowsPathSeparators()
    {
        var control = new Regex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]");
        var driveWithoutSeparator = new Regex(@"(?<![A-Za-z])[A-Z]:(?![\\/])[A-Za-z]");
        IEnumerable<string> docs = Directory
            .EnumerateFiles(Skills, "*.md", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(Root, "docs"), "*.md"))
            .Append(Path.Combine(Root, "AGENTS.md"));

        var broken = new List<string>();
        foreach (string doc in docs)
        {
            string[] lines = File.ReadAllLines(doc);
            for (int i = 0; i < lines.Length; i++)
            {
                if (control.IsMatch(lines[i]) || driveWithoutSeparator.IsMatch(lines[i]))
                {
                    broken.Add(Path.GetRelativePath(Root, doc) + ":" + (i + 1));
                }
            }
        }

        Assert.True(
            broken.Count == 0,
            "A Windows path lost its separators, or a control character is in: "
                + string.Join(", ", broken)
        );
    }

    [Fact]
    public void Skills_AreFirstPartyOrPinnedInTheVendoredManifest()
    {
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Skills, "vendored.json"))
        );
        JsonElement root = manifest.RootElement;
        Assert.Matches("^[0-9a-f]{12,40}$", root.GetProperty("commit").GetString());
        Assert.True(
            File.Exists(Path.Combine(Skills, root.GetProperty("license").GetString())),
            "The vendored skills' license file is missing."
        );
        var vendored = root.GetProperty("skills")
            .EnumerateArray()
            .Select(skill => skill.GetString())
            .ToHashSet(StringComparer.Ordinal);

        string agents = File.ReadAllText(Path.Combine(Root, "AGENTS.md"));
        var firstParty = Regex
            .Matches(agents, @"\| `\.agents/skills/([a-z0-9-]+)` \|")
            .Select(match => match.Groups[1].Value)
            .Append("csharp-solid")
            .ToHashSet(StringComparer.Ordinal);
        var folders = Directory
            .GetDirectories(Skills)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(vendored.Intersect(firstParty));
        Assert.Equal(
            folders.Order(StringComparer.Ordinal),
            vendored.Union(firstParty).Order(StringComparer.Ordinal)
        );

        // AGENTS.md lists the same vendored skills as the manifest.
        string official = agents.Substring(
            agents.IndexOf("## Official .NET skills", StringComparison.Ordinal)
        );
        var listed = Regex
            .Matches(official, "`([a-z0-9-]+)`")
            .Select(match => match.Groups[1].Value)
            .Where(folders.Contains)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(vendored.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
    }

    private static IEnumerable<string> CommandPaths(Command command, string prefix)
    {
        foreach (Command child in command.Subcommands.Where(child => !child.Hidden))
        {
            string path = prefix == null ? child.Name : prefix + " " + child.Name;
            if (child.Subcommands.Count == 0)
            {
                yield return path;
                continue;
            }

            foreach (string nested in CommandPaths(child, path))
            {
                yield return nested;
            }
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "heroes-replay.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("heroes-replay.slnx was not found.");
    }
}
