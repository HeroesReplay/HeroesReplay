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
    public void BothWorkflows_FetchThePinnedSdkPackageBeforeAnythingRestores()
    {
        // HeroesClientSDK (the memory match clock) restores from the .packages folder, filled
        // from the public GitHub Release asset with no credentials and checked against the
        // SHA-256 pinned next to its version. Without it, release.yml cannot build master.
        string config = ReadRepoFile("nuget.config");
        Assert.Contains("value=\".packages\"", config, StringComparison.Ordinal);
        Assert.Contains(
            "<package pattern=\"HeroesClientSDK\" />",
            config,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("nuget.pkg.github.com", config, StringComparison.Ordinal);
        Assert.DoesNotContain("packageSourceCredentials", config, StringComparison.Ordinal);

        string props = ReadRepoFile("Directory.Packages.props");
        Assert.Matches(@"<HeroesClientSDKVersion>\d+\.\d+\.\d+</HeroesClientSDKVersion>", props);
        Assert.Matches(@"<HeroesClientSDKSha256>[0-9a-f]{64}</HeroesClientSDKSha256>", props);
        Assert.Contains(
            "<PackageVersion Include=\"HeroesClientSDK\" Version=\"[$(HeroesClientSDKVersion)]\" />",
            props,
            StringComparison.Ordinal
        );

        string script = ReadRepoFile("tools", "restore-sdk-package.ps1");
        Assert.Contains(
            "https://github.com/HeroesReplay/HeroesClientSDK/releases/download/v$version/$name",
            script,
            StringComparison.Ordinal
        );
        Assert.Contains("HeroesClientSDKSha256", script, StringComparison.Ordinal);
        Assert.Contains("restore-sdk-package.ps1", ReadRepoFile("Directory.Build.targets"));

        foreach (string workflow in new[] { "ci.yml", "release.yml" })
        {
            string text = ReadRepoFile(".github", "workflows", workflow);
            int fetch = text.IndexOf("tools/restore-sdk-package.ps1", StringComparison.Ordinal);
            int tools = text.IndexOf("gittools/actions", StringComparison.Ordinal);
            int build = text.IndexOf("dotnet build heroes-replay.slnx", StringComparison.Ordinal);

            Assert.True(fetch > 0, workflow + " does not run tools/restore-sdk-package.ps1.");
            Assert.True(build > fetch, workflow + " builds before it fetches HeroesClientSDK.");
            Assert.True(tools < 0 || tools > fetch, workflow + " restores tools before the fetch.");
            Assert.DoesNotContain("packages: read", text, StringComparison.Ordinal);
            Assert.DoesNotContain("nuget update source", text, StringComparison.Ordinal);
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
