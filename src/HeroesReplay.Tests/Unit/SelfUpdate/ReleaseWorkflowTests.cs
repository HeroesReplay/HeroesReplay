using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.SelfUpdate;
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
            "-Version '${{ steps.version.outputs.version }}'",
            release,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Release_TagsTheMasterCommitItBuilt()
    {
        // #339: GitHub creates a missing tag on the default branch (develop) unless the release
        // names a commit, so every release was tagged on a develop commit it never shipped.
        string release = ReadRepoFile(".github", "workflows", "release.yml");
        const string Action = "softprops/action-gh-release";
        const string TagName = "${{ steps.version.outputs.version }}";
        int upload = release.IndexOf(Action, StringComparison.Ordinal);
        Assert.True(upload > 0, "release.yml does not publish with " + Action);
        Assert.Equal(upload, release.LastIndexOf(Action, StringComparison.Ordinal));

        string publish = release[upload..];
        Assert.Contains("tag_name: " + TagName, publish, StringComparison.Ordinal);
        Assert.Contains("target_commitish: ${{ github.sha }}", publish, StringComparison.Ordinal);

        // The build is that same commit: the checkout takes the pushed sha, not another ref.
        Assert.DoesNotMatch(@"(?m)^\s+ref:", release);

        // GitHub ignores target_commitish when the tag exists, so the run refuses a tag with
        // that name on any other commit before it publishes.
        int guard = release.IndexOf("git rev-list -n 1 $env:TAG", StringComparison.Ordinal);
        Assert.True(guard > 0 && guard < upload, "release.yml does not check the tag first.");
        Assert.Contains("TAG: " + TagName, release, StringComparison.Ordinal);
        Assert.Contains("$commit -ne $env:GITHUB_SHA", release, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", release, StringComparison.Ordinal);

        // Nothing else creates or moves a tag or a release.
        Assert.DoesNotMatch(@"git tag\s+(?!--list\b)", release);
        foreach (string other in new[] { "gh release", "git push", "create-release", "/releases" })
        {
            Assert.DoesNotContain(other, release, StringComparison.Ordinal);
        }
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
    public void BothWorkflows_InstallTheSdkGlobalJsonPins()
    {
        // #462: the SDK carries the runtime a release is built on, so it is pinned and
        // Dependabot moves it, never whatever 11.0.x the runner has that day.
        using var global = JsonDocument.Parse(ReadRepoFile("global.json"));
        JsonElement sdk = global.RootElement.GetProperty("sdk");
        Assert.Matches(@"^11\.0\.\d{3}$", sdk.GetProperty("version").GetString());
        // #522: not latestPatch, which setup-dotnet turns into a channel (11.0.1xx) that can point at
        // an SDK Microsoft has staged but not released (HTTP 409); patch installs this exact version.
        Assert.Equal("patch", sdk.GetProperty("rollForward").GetString());

        foreach (string workflow in new[] { "ci.yml", "release.yml" })
        {
            string text = ReadRepoFile(".github", "workflows", workflow);
            Assert.Contains("global-json-file: global.json", text, StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet-version:", text, StringComparison.Ordinal);
        }

        Assert.Contains("global.json", ReadRepoFile("tools", "bootstrap-workstation.ps1"));
    }

    [Fact]
    public void Dependabot_MovesTheSdkAndPackagesButNotHeroesClientSdk()
    {
        string config = ReadRepoFile(".github", "dependabot.yml");

        foreach (string ecosystem in new[] { "dotnet-sdk", "nuget", "github-actions" })
        {
            Assert.Contains("package-ecosystem: " + ecosystem, config, StringComparison.Ordinal);
        }

        Assert.Contains("- dependency-name: HeroesClientSDK", config, StringComparison.Ordinal);
        foreach (string group in new[] { "microsoft-extensions:", "opentelemetry:", "kiota:" })
        {
            Assert.Contains(group, config, StringComparison.Ordinal);
        }

        Assert.Contains("version-update:semver-major", config, StringComparison.Ordinal);
        Assert.Contains("target-branch: develop", config, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_NamesTheRuntimeFromRuntimeTxtInItsNotes()
    {
        string package = ReadRepoFile("tools", "package-release.ps1");
        string verify = ReadRepoFile("tools", "verify-release.ps1");
        string release = ReadRepoFile(".github", "workflows", "release.yml");

        // version.txt stays the tag alone: the update compares it to the release tag.
        Assert.Contains(
            "'runtime.txt') -Value $runtime -NoNewline",
            package,
            StringComparison.Ordinal
        );
        Assert.Contains("'runtime.txt'", verify, StringComparison.Ordinal);
        Assert.Contains(
            "@('check', 'runtime', '--output', 'json')",
            verify,
            StringComparison.Ordinal
        );

        int read = release.IndexOf("GetEntry('runtime.txt')", StringComparison.Ordinal);
        int upload = release.IndexOf("softprops/action-gh-release", StringComparison.Ordinal);
        Assert.True(read > 0 && read < upload, "release.yml does not read runtime.txt first.");
        Assert.Contains(
            "Runtime: ${{ steps.runtime.outputs.runtime }}",
            release[upload..],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void RuntimeCurrency_RunsWeeklyAgainstTheDotnetReleaseMetadata()
    {
        string workflow = ReadRepoFile(".github", "workflows", "runtime-currency.yml");
        string script = ReadRepoFile("tools", "check-runtime-currency.ps1");

        Assert.Matches(@"cron: ""\d+ \d+ \* \* 3""", workflow);
        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("issues: write", workflow, StringComparison.Ordinal);
        Assert.Contains("tools/check-runtime-currency.ps1", workflow, StringComparison.Ordinal);

        Assert.Contains(
            "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$Channel/releases.json",
            script,
            StringComparison.Ordinal
        );
        Assert.Contains("'latest-runtime'", script, StringComparison.Ordinal);
        Assert.Contains("$_.security -eq $true", script, StringComparison.Ordinal);
        Assert.Contains("(?m)^Runtime: ", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Scripts_KeepSecretsOutAndCheckTheObsBundle()
    {
        string package = ReadRepoFile("tools", "package-release.ps1");
        string verify = ReadRepoFile("tools", "verify-release.ps1");

        Assert.Contains("-r win-x64 --self-contained true", package, StringComparison.Ordinal);
        Assert.Contains("appsettings.secrets.json", package, StringComparison.Ordinal);
        Assert.Contains("-Filter service.json | Remove-Item", package, StringComparison.Ordinal);
        Assert.Contains(
            "version.txt') -Value $Version -NoNewline",
            package,
            StringComparison.Ordinal
        );

        Assert.Contains("obs\\bundle.manifest", verify, StringComparison.Ordinal);
        Assert.Contains("'HeroesClientSDK.dll'", verify, StringComparison.Ordinal);
        Assert.Contains("'hostfxr.dll'", verify, StringComparison.Ordinal);
        Assert.Contains("'coreclr.dll'", verify, StringComparison.Ordinal);
        Assert.Contains("'--help'", verify, StringComparison.Ordinal);

        // #308: the published build writes the schema 2 manifest, and the check hashes every file.
        Assert.Contains("obs bundle --install $out --write", package, StringComparison.Ordinal);
        Assert.True(
            package.IndexOf("obs bundle --install $out --write", StringComparison.Ordinal)
                < package.IndexOf("Compress-Archive", StringComparison.Ordinal),
            "package-release.ps1 writes the manifest after the zip is made."
        );
        Assert.Contains("Get-FileHash", verify, StringComparison.Ordinal);
        Assert.Contains("collectionSha256", verify, StringComparison.Ordinal);
        Assert.Contains(
            "@('obs', 'bundle', '--install', $extract)",
            verify,
            StringComparison.Ordinal
        );
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

    /// <summary>
    /// tools/next-version.ps1 picks the release tag: the newest vX.Y.Z bumped by the release PR's
    /// label or "+semver:" (patch by default), ignoring the v1.0.0-NNNN tags from before semantic
    /// versioning, whose first semantic release is v1.1.0.
    /// </summary>
    [Theory]
    [InlineData("v1.0.0-1136,v1.0.0-1137", "", "", "", "v1.1.0")]
    [InlineData("v1.0.0-1137", "release:major", "", "", "v2.0.0")]
    [InlineData("v1.0.0-1137,v1.1.0", "", "", "", "v1.1.1")]
    [InlineData("v1.0.0-1137,v1.1.0", "release:minor", "", "", "v1.2.0")]
    [InlineData("v1.1.0,v1.1.1", "dependencies, Release:Major", "", "", "v2.0.0")]
    [InlineData("v1.9.3,v1.10.0,v1.2.7", "", "", "", "v1.10.1")]
    [InlineData("v1.4.2", "", "Merge pull request #600\n\nRelease +semver: minor", "", "v1.5.0")]
    [InlineData("v1.4.2", "release:patch", "", "", "v1.4.3")]
    [InlineData("v1.4.2,v2.0.0-rc.1", "", "", "", "v1.4.3")]
    // A re-run of a master commit that is already tagged keeps its tag.
    [InlineData("v1.4.2,v1.4.3", "release:major", "", "v1.4.3", "v1.4.3")]
    public void NextVersion_BumpsTheNewestSemVerTag(
        string tags,
        string labels,
        string message,
        string headTags,
        string expected
    )
    {
        string tag = RunNextVersion(tags, labels, message, headTags);

        Assert.Equal(expected, tag);
        Assert.True(ReleaseVersion.TryParse(tag, out ReleaseVersion next));
        foreach (string old in tags.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            // Every release orders above the stable tags before it and the v1.0.0-NNNN ones.
            bool legacy = old.StartsWith("v1.0.0-", StringComparison.Ordinal);
            if (
                old != headTags
                && ReleaseVersion.TryParse(old, out ReleaseVersion earlier)
                && (legacy || !earlier.IsPreRelease)
            )
            {
                Assert.True(
                    next.CompareTo(earlier) > 0,
                    $"{tag} does not order above {old}."
                );
            }
        }
    }

    [Fact]
    public void Package_StampsTheAssembliesWithTheSemVerTag()
    {
        string package = ReadRepoFile("tools", "package-release.ps1");

        Assert.Contains("\"-p:Version=$assemblyVersion\"", package, StringComparison.Ordinal);
        Assert.Contains("-notmatch $semVer", package, StringComparison.Ordinal);
        Assert.Contains("<VersionSuffix>dev</VersionSuffix>", ReadRepoFile("Directory.Build.props"));
        Assert.False(File.Exists(Path.Combine(RepoRoot(), "GitVersion.yml")));
        Assert.DoesNotContain("gitversion", ReadRepoFile(".github", "workflows", "release.yml"));
    }

    private static string RunNextVersion(
        string tags,
        string labels,
        string message,
        string headTags
    )
    {
        static string List(string csv) =>
            "@("
            + string.Join(
                ",",
                csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => "'" + item.Trim() + "'")
            )
            + ")";

        string script = Path.Combine(RepoRoot(), "tools", "next-version.ps1");
        string command =
            $"& '{script}' -Tags {List(tags)} -HeadTags {List(headTags)} "
            + $"-Labels '{labels}' -Message \"{message.Replace("\n", "`n")}\"";
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment.Remove("GITHUB_OUTPUT");
        using Process process = Process.Start(start);
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"next-version.ps1 exited {process.ExitCode}: {error}");
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Last();
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
