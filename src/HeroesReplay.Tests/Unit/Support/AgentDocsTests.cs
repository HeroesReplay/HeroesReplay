using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using HeroesReplay.CLI.Commands;
using HeroesReplay.Core.Dependencies;
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
