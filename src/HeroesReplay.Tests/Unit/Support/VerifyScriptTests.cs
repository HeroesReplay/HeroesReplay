using System;
using System.IO;
using Xunit;

namespace HeroesReplay.Tests.Unit.Support;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class VerifyScriptTests
{
    [Fact]
    public void VerifyScript_BuildsAndRunsUnitTestsOnly()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "verify.ps1"));

        Assert.Contains("dotnet build heroes-replay.slnx", text, StringComparison.Ordinal);
        Assert.Contains("dotnet test heroes-replay.slnx", text, StringComparison.Ordinal);
        Assert.Contains("-p:TestCategory=Unit", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TestCategory=Integration", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TestCategory=Smoke", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GitHooks_InvokeVerifyForCommitAndDevelopPush()
    {
        string root = RepoRoot();
        string preCommit = File.ReadAllText(Path.Combine(root, "tools", "git-hooks", "pre-commit"));
        string prePush = File.ReadAllText(Path.Combine(root, "tools", "git-hooks", "pre-push"));

        Assert.Contains("tools/verify.ps1", preCommit, StringComparison.Ordinal);
        Assert.Contains("-Hook pre-commit", preCommit, StringComparison.Ordinal);
        Assert.Contains("tools/verify.ps1", prePush, StringComparison.Ordinal);
        Assert.Contains("-Hook pre-push", prePush, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        foreach (
            string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
        )
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "heroes-replay.slnx")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException("Could not find heroes-replay.slnx.");
    }
}
