using System;
using System.IO;
using Xunit;

namespace HeroesReplay.Tests.Unit.SelfUpdate;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReleaseWorkflowTests
{
    private const string PackageScript = "tools/package-release.ps1";
    private const string VerifyScript = "tools/verify-release.ps1";

    [Fact]
    public void BothWorkflows_BuildTheZipWithThePackageScript()
    {
        string ci = ReadRepoFile(".github", "workflows", "ci.yml");
        string release = ReadRepoFile(".github", "workflows", "release.yml");

        Assert.Contains(PackageScript, ci, StringComparison.Ordinal);
        Assert.Contains(PackageScript, release, StringComparison.Ordinal);
        Assert.DoesNotContain("Compress-Archive", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("Compress-Archive", release, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet publish", release, StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_VerifiesTheZipAfterPackagingInTheRequiredJob()
    {
        string ci = ReadRepoFile(".github", "workflows", "ci.yml");
        int package = ci.IndexOf(PackageScript, StringComparison.Ordinal);
        int verify = ci.IndexOf(VerifyScript, StringComparison.Ordinal);

        // Branch rules require the check by this job name.
        Assert.Contains("name: build-and-test", ci, StringComparison.Ordinal);
        Assert.True(package > 0, "ci.yml does not run " + PackageScript);
        Assert.True(verify > package, "ci.yml does not run " + VerifyScript + " after packaging.");
    }

    [Fact]
    public void Release_RunsUnitAndSmokeAndVerifiesBeforeTheUpload()
    {
        string release = ReadRepoFile(".github", "workflows", "release.yml");
        int verify = release.IndexOf(VerifyScript, StringComparison.Ordinal);
        int upload = release.IndexOf("softprops/action-gh-release", StringComparison.Ordinal);

        Assert.Contains("-p:TestCategory=Unit", release, StringComparison.Ordinal);
        Assert.Contains("-p:TestCategory=Smoke", release, StringComparison.Ordinal);
        Assert.True(verify > 0 && upload > verify);
        Assert.Contains(
            "-Version 'v${{ steps.gitversion.outputs.semVer }}'",
            release,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void BothWorkflows_AuthenticateGitHubPackagesBeforeTheBuild()
    {
        // HeroesClientSDK (the memory match clock) restores from GitHub Packages, which needs a
        // token even for a public package. Without it, release.yml cannot build master.
        string config = ReadRepoFile("nuget.config");
        Assert.Contains(
            "https://nuget.pkg.github.com/HeroesReplay/index.json",
            config,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<package pattern=\"HeroesClientSDK\" />",
            config,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("packageSourceCredentials", config, StringComparison.Ordinal);

        foreach (string workflow in new[] { "ci.yml", "release.yml" })
        {
            string text = ReadRepoFile(".github", "workflows", workflow);
            int auth = text.IndexOf("dotnet nuget update source github", StringComparison.Ordinal);
            int build = text.IndexOf("dotnet build heroes-replay.slnx", StringComparison.Ordinal);

            Assert.Contains("packages: read", text, StringComparison.Ordinal);
            Assert.Contains(
                "--password ${{ secrets.GITHUB_TOKEN }}",
                text,
                StringComparison.Ordinal
            );
            Assert.True(
                auth > 0 && build > auth,
                workflow + " does not authenticate before the build."
            );
        }
    }

    [Fact]
    public void Scripts_KeepSecretsOutAndCheckTheObsBundle()
    {
        string package = ReadRepoFile("tools", "package-release.ps1");
        string verify = ReadRepoFile("tools", "verify-release.ps1");

        Assert.Contains("-r win-x64 --self-contained false", package, StringComparison.Ordinal);
        Assert.Contains("appsettings.secrets.json", package, StringComparison.Ordinal);
        Assert.Contains("-Filter service.json | Remove-Item", package, StringComparison.Ordinal);
        Assert.Contains(
            "version.txt') -Value $Version -NoNewline",
            package,
            StringComparison.Ordinal
        );

        Assert.Contains("obs\\bundle.manifest", verify, StringComparison.Ordinal);
        Assert.Contains("'HeroesClientSDK.dll'", verify, StringComparison.Ordinal);
        Assert.Contains("'--help'", verify, StringComparison.Ordinal);
        foreach (
            string secret in new[]
            {
                "'service.json'",
                "'appsettings.secrets.json'",
                "'client_secrets.json'",
            }
        )
        {
            Assert.Contains(secret, verify, StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(params string[] parts)
    {
        string path = Path.Combine(RepoRoot(), Path.Combine(parts));
        Assert.True(File.Exists(path), path + " is missing.");
        return File.ReadAllText(path);
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
