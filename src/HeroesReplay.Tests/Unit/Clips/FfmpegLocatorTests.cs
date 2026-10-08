using System;
using System.Collections.Generic;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Dependencies;
using Xunit;

namespace HeroesReplay.Tests.Unit.Clips;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class FfmpegLocatorTests
{
    private const string Configured = @"D:\custom\ffmpeg";
    private const string Installed = @"C:\heroesreplay\tools\ffmpeg";
    private const string OnPath = @"C:\Tools\bin";

    [Fact]
    public void Resolve_PrefersConfiguredThenInstalledThenLegacyThenPath()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Configured + @"\ffmpeg.exe",
            Installed + @"\ffmpeg.exe",
            @"C:\ffmpeg\bin\ffmpeg.exe",
            OnPath + @"\ffmpeg.exe",
        };

        Assert.Equal(
            (Configured + @"\ffmpeg.exe", FfmpegSource.Configured),
            Resolve(files, "ffmpeg")
        );
        files.Remove(Configured + @"\ffmpeg.exe");
        Assert.Equal(
            (Installed + @"\ffmpeg.exe", FfmpegSource.Installed),
            Resolve(files, "ffmpeg")
        );
        files.Remove(Installed + @"\ffmpeg.exe");
        Assert.Equal((@"C:\ffmpeg\bin\ffmpeg.exe", FfmpegSource.Legacy), Resolve(files, "ffmpeg"));
        files.Remove(@"C:\ffmpeg\bin\ffmpeg.exe");
        Assert.Equal((OnPath + @"\ffmpeg.exe", FfmpegSource.Path), Resolve(files, "ffmpeg"));
        files.Remove(OnPath + @"\ffmpeg.exe");
        Assert.Equal((null, FfmpegSource.Missing), Resolve(files, "ffmpeg"));
    }

    [Fact]
    public void Resolve_LooksForEachToolOnItsOwn()
    {
        // ffprobe is only in the legacy folder, ffmpeg only in the install folder.
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Installed + @"\ffmpeg.exe",
            @"C:\ffmpeg\bin\ffprobe.exe",
        };

        Assert.Equal(FfmpegSource.Installed, Resolve(files, "ffmpeg").Source);
        Assert.Equal(FfmpegSource.Legacy, Resolve(files, "ffprobe").Source);
    }

    [Fact]
    public void Find_WhenMissing_ReturnsTheBareName()
    {
        var locator = new FfmpegLocator(null, Installed, string.Empty, _ => false);

        Assert.Equal("ffprobe", locator.Find("ffprobe"));
        Assert.False(locator.Resolve("ffprobe").Found);
    }

    [Fact]
    public void Resolve_SkipsABlankConfiguredFolderAndOddPathEntries()
    {
        var locator = new FfmpegLocator(
            "  ",
            null,
            ";\"C:\\Quoted Tools\";;C:\\Bad|Dir;",
            path => path == @"C:\Quoted Tools\ffmpeg.exe"
        );

        Assert.Null(locator.ConfiguredDirectory);
        Assert.Equal(
            new FfmpegResolution("ffmpeg", @"C:\Quoted Tools\ffmpeg.exe", FfmpegSource.Path),
            locator.Resolve("ffmpeg")
        );
    }

    [Fact]
    public void From_UsesTheDependenciesFolder()
    {
        FfmpegLocator locator = FfmpegLocator.From(
            new ClipSettings { FfmpegDirectory = Configured },
            new DependencySettings { Directory = @"E:\tools" }
        );

        Assert.Equal(Configured, locator.ConfiguredDirectory);
        Assert.Equal(@"E:\tools\ffmpeg", locator.InstallDirectory);
        Assert.Equal(
            @"Clips:FfmpegDirectory D:\custom\ffmpeg, E:\tools\ffmpeg, C:\ffmpeg\bin, PATH",
            locator.DescribeSearch()
        );
    }

    [Fact]
    public void From_WithoutSettings_UsesTheDefaultInstallFolder()
    {
        FfmpegLocator locator = FfmpegLocator.From(null, null);

        Assert.Null(locator.ConfiguredDirectory);
        Assert.Equal(Installed, locator.InstallDirectory);
    }

    private static (string Path, FfmpegSource Source) Resolve(
        IReadOnlySet<string> files,
        string tool
    )
    {
        FfmpegResolution resolution = new FfmpegLocator(
            Configured,
            Installed,
            @"C:\Windows;" + OnPath,
            files.Contains
        ).Resolve(tool);
        return (resolution.Path, resolution.Source);
    }
}
