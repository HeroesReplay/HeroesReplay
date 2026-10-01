using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace HeroesReplay.Tests.Unit.SelfUpdate;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReleaseBundleTests
{
    [Theory]
    [InlineData(@"deploy\production\AGENTS.md")]
    [InlineData(@"deploy\production\CLAUDE.md")]
    [InlineData(@"deploy\production\.mcp.json")]
    [InlineData(@"deploy\production\.grok\config.toml")]
    [InlineData(@"tools\ensure-secrets.ps1")]
    [InlineData(@"tools\fill-secrets-from-op.ps1")]
    public void ProductionAgentFiles_ArePublishedWithTheRelease(string relativePath)
    {
        string root = FindRepoRoot();
        string project = File.ReadAllText(
            Path.Combine(root, "src", "HeroesReplay.CLI", "HeroesReplay.CLI.csproj")
        );

        Assert.True(File.Exists(Path.Combine(root, relativePath)), relativePath);
        Assert.Contains(@"..\..\" + relativePath, project);
    }

    [Fact]
    public void ProductionRunbook_StartsFromTheInstallWithProdSettings()
    {
        string runbook = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "deploy", "production", "AGENTS.md")
        );

        Assert.Contains(@"C:\heroesreplay\app", runbook);
        Assert.Contains("HEROES_REPLAY_ENV = 'prod'", runbook);
        Assert.Contains("ensure-secrets.ps1", runbook);
        Assert.Contains("services start", runbook);
        Assert.Contains("services stop", runbook);
        Assert.Contains("HeroesReplay-live", runbook);
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "heroes-replay.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("heroes-replay.slnx");
    }
}
