using System;
using System.Diagnostics;
using System.IO;
using HeroesReplay.Core.Obs.Inspection;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Inspection;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsPathCheckTests
{
    private const string Logo = @"C:\heroesreplay\app\obs\hots-logo.png";
    private const string LogoAtTarget = @"C:\SaltySadism\app\obs\hots-logo.png";

    [Fact]
    public void AFileBehindAnUntraversableJunction_IsFoundAtTheJunctionsTarget()
    {
        ObsPathCheck found = ObsPathCheck.Of(Logo, FakeObsFileSystem.OverSsh().With(LogoAtTarget));

        Assert.Equal(ObsPathState.Exists, found.State);
        Assert.Equal(LogoAtTarget, found.Resolved);
        Assert.Equal(@"C:\heroesreplay", found.Link);
        Assert.Equal(@"C:\SaltySadism", found.Target);
    }

    [Fact]
    public void AFileMissingAtTheJunctionsTarget_IsMissing()
    {
        ObsPathCheck found = ObsPathCheck.Of(
            Logo,
            FakeObsFileSystem.OverSsh().With(@"C:\SaltySadism\app\obs\other.png")
        );

        Assert.Equal(ObsPathState.Missing, found.State);
        Assert.Equal(LogoAtTarget, found.Resolved);
        Assert.Equal(@"C:\heroesreplay", found.Link);
    }

    [Fact]
    public void AJunctionWhoseTargetCannotBeRead_IsUnverifiable_AndNamed()
    {
        ObsPathCheck found = ObsPathCheck.Of(
            Logo,
            FakeObsFileSystem.OverSsh(targetReadable: false).With(LogoAtTarget)
        );

        Assert.Equal(ObsPathState.Unverifiable, found.State);
        Assert.Equal(@"C:\heroesreplay", found.Link);
        Assert.Null(found.Target);
        Assert.Contains("denied", found.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void APathTheFileSystemWillNotAnswerFor_WithNoLinkOnTheWay_IsUnverifiable()
    {
        // As for a reparse point that is not a junction or symbolic link: no target to read.
        var refusing = new RefusingFileSystem(new FakeObsFileSystem());

        ObsPathCheck found = ObsPathCheck.Of(@"C:\share\a.png", refusing);

        Assert.Equal(ObsPathState.Unverifiable, found.State);
        Assert.Null(found.Link);
        Assert.Equal(FakeObsFileSystem.UntrustedMountPoint, found.Reason);
    }

    [Fact]
    public void AMissingFile_WithNoLinkOnTheWay_IsMissing()
    {
        ObsPathCheck found = ObsPathCheck.Of(
            @"C:\heroesreplay\app\obs\hots-logo.png",
            new FakeObsFileSystem().With(@"C:\heroesreplay\app\obs\other.png")
        );

        Assert.Equal(ObsPathState.Missing, found.State);
        Assert.Null(found.Link);
        Assert.Equal(Logo, found.Resolved);
    }

    [Fact]
    public void AFileThatExists_NamesNoLink()
    {
        ObsPathCheck found = ObsPathCheck.Of(Logo, new FakeObsFileSystem().With(Logo));

        Assert.Equal(new ObsPathCheck(ObsPathState.Exists, Logo), found);
    }

    [Fact]
    public void ALoopOfLinks_EndsUnverifiable()
    {
        var files = new FakeObsFileSystem();
        files.Links[@"C:\a"] = @"C:\b";
        files.Links[@"C:\b"] = @"C:\a";
        files.Untraversable.Add(@"C:\a");
        files.Untraversable.Add(@"C:\b");

        ObsPathCheck found = ObsPathCheck.Of(@"C:\a\x.png", files);

        Assert.Equal(ObsPathState.Unverifiable, found.State);
        Assert.Contains("links on the way", found.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ALinkUnderTheTarget_IsFollowedToo_AndTheFirstLinkIsNamed()
    {
        FakeObsFileSystem files = FakeObsFileSystem.OverSsh().With(@"D:\obs-assets\hots-logo.png");
        files.Links[@"C:\SaltySadism\app\obs"] = @"D:\obs-assets";
        files.Untraversable.Add(@"C:\SaltySadism\app\obs");

        ObsPathCheck found = ObsPathCheck.Of(Logo, files);

        Assert.Equal(ObsPathState.Exists, found.State);
        Assert.Equal(@"D:\obs-assets\hots-logo.png", found.Resolved);
        Assert.Equal(@"C:\heroesreplay", found.Link);
    }

    [Fact]
    public void TheMachinesFileSystem_ReadsAJunctionWithoutFollowingIt()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hr-obs-junction-" + Path.GetRandomFileName()
        );
        string target = Path.Combine(root, "target");
        string link = Path.Combine(root, "link");
        Directory.CreateDirectory(Path.Combine(target, "obs"));
        File.WriteAllText(Path.Combine(target, "obs", "hots-logo.png"), "png");
        try
        {
            Junction(link, target);
            IObsFileSystem files = ObsFileSystem.Instance;

            Assert.True(files.Exists(Path.Combine(link, "obs", "hots-logo.png")));
            Assert.False(files.Exists(Path.Combine(link, "obs", "missing.png")));
            Assert.Equal(target, files.LinkTarget(link));
            Assert.Null(files.LinkTarget(target));
            Assert.Null(files.LinkTarget(Path.Combine(root, "nothing-here")));

            ObsPathCheck missing = ObsPathCheck.Of(Path.Combine(link, "obs", "missing.png"), files);
            Assert.Equal(ObsPathState.Missing, missing.State);
            Assert.Equal(link, missing.Link);
            Assert.Equal(Path.Combine(target, "obs", "missing.png"), missing.Resolved);
        }
        finally
        {
            if (Directory.Exists(link))
            {
                // Removes the junction only, never what it points at.
                Directory.Delete(link);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    private static void Junction(string link, string target)
    {
        using Process mklink = Process.Start(
            new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        );
        string output = mklink.StandardOutput.ReadToEnd() + mklink.StandardError.ReadToEnd();
        mklink.WaitForExit();
        Assert.True(mklink.ExitCode == 0, "mklink /J failed: " + output);
    }

    /// <summary>Refuses every Exists below <c>C:\share</c> and knows no link.</summary>
    private sealed class RefusingFileSystem(FakeObsFileSystem inner) : IObsFileSystem
    {
        public bool Exists(string path) =>
            path.StartsWith(@"C:\share\", StringComparison.OrdinalIgnoreCase)
                ? throw new IOException(FakeObsFileSystem.UntrustedMountPoint)
                : inner.Exists(path);

        public string LinkTarget(string path) => inner.LinkTarget(path);
    }
}
