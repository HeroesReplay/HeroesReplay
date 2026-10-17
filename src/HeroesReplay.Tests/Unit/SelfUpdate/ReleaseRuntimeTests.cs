using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using HeroesReplay.Core.SelfUpdate;
using Xunit;

namespace HeroesReplay.Tests.Unit.SelfUpdate;

/// <summary>The runtime a release was built and tested on, and <c>check runtime</c> (#462).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReleaseRuntimeTests
{
    /// <summary>What tools/package-release.ps1 reads from the check's JSON detail.</summary>
    private const string PackagePattern = @"^(?<runtime>\.NET \d+\.\d+\.\d+\S*?)(?:,|\.?(?:\s|$))";

    [Fact]
    public void Current_IsTheFrameworkDescription()
    {
        Assert.Equal(RuntimeInformation.FrameworkDescription, ReleaseRuntime.Current);
        Assert.StartsWith(".NET ", ReleaseRuntime.Current, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".NET 10.0.13", "10.0.13")]
    [InlineData("10.0.12", "10.0.12")]
    [InlineData(".NET 10.0.0-rc.1.25451.107", "10.0.0")]
    public void ParseVersion_ReadsTheRuntimeVersion(string description, string expected)
    {
        Assert.Equal(Version.Parse(expected), ReleaseRuntime.ParseVersion(description));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".NET")]
    public void ParseVersion_IsNullWithoutAVersion(string description)
    {
        Assert.Null(ReleaseRuntime.ParseVersion(description));
    }

    [Fact]
    public void Read_TrimsRuntimeTxtAndIsNullWithoutOne()
    {
        string install = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-runtime-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(install);
        try
        {
            Assert.Null(ReleaseRuntime.Read(install));
            Assert.Null(ReleaseRuntime.Read(null));

            string file = Path.Combine(install, ReleaseRuntime.FileName);
            File.WriteAllText(file, "  ");
            Assert.Null(ReleaseRuntime.Read(install));

            File.WriteAllText(file, ".NET 10.0.13\r\n");
            Assert.Equal(".NET 10.0.13", ReleaseRuntime.Read(install));
        }
        finally
        {
            Directory.Delete(install, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_WithoutRuntimeTxt_SaysSoAndPasses()
    {
        ReleaseRuntimeReport report = ReleaseRuntime.Evaluate(
            ".NET 10.0.13",
            @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.13\",
            null
        );

        Assert.False(report.OlderThanRelease);
        Assert.Null(report.Release);
        Assert.Equal(
            @".NET 10.0.13, from C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.13. No runtime.txt beside the exe: a source build, or a release from before #462.",
            report.Detail
        );
    }

    [Theory]
    [InlineData(".NET 10.0.13", ".NET 10.0.13")]
    [InlineData(".NET 10.0.14", ".NET 10.0.13")]
    public void Evaluate_TheReleaseRuntimeOrNewer_Passes(string current, string release)
    {
        ReleaseRuntimeReport report = ReleaseRuntime.Evaluate(current, @"C:\rt", release);

        Assert.False(report.OlderThanRelease);
        Assert.Equal(release, report.Release);
        Assert.Equal(
            $@"{current}, from C:\rt. This release was built and tested on {release} (runtime.txt).",
            report.Detail
        );
    }

    [Fact]
    public void Evaluate_AnOlderMachineRuntime_IsOlderThanTheRelease()
    {
        // A framework-dependent install on a machine Microsoft Update has not serviced yet.
        ReleaseRuntimeReport report = ReleaseRuntime.Evaluate(
            ".NET 10.0.12",
            @"C:\rt",
            ".NET 10.0.13"
        );

        Assert.True(report.OlderThanRelease);
        Assert.StartsWith(".NET 10.0.12, from C:\\rt, is older than .NET 10.0.13", report.Detail);
    }

    [Theory]
    [InlineData(".NET 10.0.13", @"C:\rt", null)]
    [InlineData(".NET 10.0.13", null, null)]
    [InlineData(".NET 10.0.12", @"C:\rt", ".NET 10.0.13")]
    [InlineData(".NET 10.0.0-rc.1.25451.107", "", ".NET 10.0.0")]
    public void Evaluate_DetailStartsWithTheRuntimePackageReleaseReads(
        string current,
        string folder,
        string release
    )
    {
        string detail = ReleaseRuntime.Evaluate(current, folder, release).Detail;

        Match match = Regex.Match(detail, PackagePattern);
        Assert.True(match.Success, detail);
        Assert.Equal(current, match.Groups["runtime"].Value);
    }

    [Fact]
    public void PackageRelease_ReadsTheRuntimeWithTheSamePattern()
    {
        string script = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "package-release.ps1"));

        Assert.Contains("check runtime --output json", script, StringComparison.Ordinal);
        Assert.Contains("'" + PackagePattern + "'", script, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "heroes-replay.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
