using System;
using System.Collections.Generic;
using HeroesReplay.Core.Dependencies;
using Xunit;

namespace HeroesReplay.Tests.Unit.Dependencies;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class DependencyInstallDecisionTests
{
    private static readonly DependencyPin Pin = new(
        "ffmpeg",
        "9.0.2",
        "test",
        "https://example.test/ffmpeg.zip",
        100,
        new string('a', 64),
        new[] { "ffmpeg.exe", "ffprobe.exe" }
    );

    private static readonly DependencyInstallRecord Installed = new(
        "ffmpeg",
        "9.0.2",
        new string('a', 64),
        "https://example.test/ffmpeg.zip",
        new[]
        {
            new DependencyInstalledFile("ffmpeg.exe", 10, new string('b', 64)),
            new DependencyInstalledFile("ffprobe.exe", 20, new string('c', 64)),
        },
        DateTimeOffset.UnixEpoch
    );

    private static readonly Dictionary<string, long?> OnDisk = new()
    {
        ["ffmpeg.exe"] = 10,
        ["ffprobe.exe"] = 20,
    };

    [Fact]
    public void ThePinnedBuildInPlace_IsLeftAlone()
    {
        DependencyInstallDecision decision = Decide(Installed, OnDisk);

        Assert.False(decision.Install);
        Assert.Equal("ffmpeg 9.0.2 is already installed.", decision.Reason);
    }

    [Fact]
    public void NoRecord_Installs()
    {
        // Exes copied in by hand have no record, so the pinned build replaces them.
        Assert.True(Decide(null, OnDisk).Install);
    }

    [Fact]
    public void AnotherVersion_Installs()
    {
        DependencyInstallDecision decision = Decide(Installed with { Version = "8.1.2" }, OnDisk);

        Assert.True(decision.Install);
        Assert.Contains("8.1.2", decision.Reason);
        Assert.Contains("9.0.2", decision.Reason);
    }

    [Fact]
    public void AnotherArchiveForTheSameVersion_Installs()
    {
        Assert.True(Decide(Installed with { Sha256 = new string('f', 64) }, OnDisk).Install);
    }

    [Fact]
    public void TheHashComparison_IgnoresCase()
    {
        Assert.False(Decide(Installed with { Sha256 = new string('A', 64) }, OnDisk).Install);
    }

    [Fact]
    public void AMissingExe_Installs()
    {
        DependencyInstallDecision decision = Decide(
            Installed,
            new Dictionary<string, long?> { ["ffmpeg.exe"] = 10 }
        );

        Assert.True(decision.Install);
        Assert.Equal("ffprobe.exe is missing.", decision.Reason);
    }

    [Fact]
    public void AnExeOfAnotherSize_Installs()
    {
        DependencyInstallDecision decision = Decide(
            Installed,
            new Dictionary<string, long?> { ["ffmpeg.exe"] = 11, ["ffprobe.exe"] = 20 }
        );

        Assert.True(decision.Install);
        Assert.Contains("ffmpeg.exe", decision.Reason);
    }

    [Fact]
    public void ARecordWithoutAPinnedFile_Installs()
    {
        DependencyInstallRecord partial = Installed with
        {
            Files = new[] { new DependencyInstalledFile("ffmpeg.exe", 10, new string('b', 64)) },
        };

        Assert.True(Decide(partial, OnDisk).Install);
    }

    private static DependencyInstallDecision Decide(
        DependencyInstallRecord record,
        IReadOnlyDictionary<string, long?> files
    ) =>
        DependencyInstallDecision.Decide(
            Pin,
            record,
            name => files.TryGetValue(name, out long? length) ? length : null
        );
}
