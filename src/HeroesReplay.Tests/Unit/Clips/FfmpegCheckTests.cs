using HeroesReplay.Core.Clips;
using Xunit;

namespace HeroesReplay.Tests.Unit.Clips;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class FfmpegCheckTests
{
    private const string Searched = @"C:\heroesreplay\tools\ffmpeg, C:\ffmpeg\bin, PATH";
    private const string Ffmpeg = @"C:\heroesreplay\tools\ffmpeg\ffmpeg.exe";
    private const string Ffprobe = @"C:\heroesreplay\tools\ffmpeg\ffprobe.exe";

    [Fact]
    public void Evaluate_ThePinnedBuild_Passes()
    {
        FfmpegCheckReport report = FfmpegCheck.Evaluate(
            "9.0.2",
            new[]
            {
                Found("ffmpeg", Ffmpeg, "9.0.2-essentials_build-www.gyan.dev", true),
                Found("ffprobe", Ffprobe, "9.0.2-essentials_build-www.gyan.dev", null),
            },
            Searched
        );

        Assert.True(report.Ok);
        Assert.False(report.Warning);
        Assert.StartsWith("The pinned 9.0.2.", report.Detail);
        Assert.Contains(
            Ffmpeg + " (deps install): ffmpeg version 9.0.2-essentials_build-www.gyan.dev",
            report.Detail
        );
        Assert.Contains(Ffprobe, report.Detail);
    }

    [Fact]
    public void Evaluate_AMissingTool_Fails()
    {
        FfmpegCheckReport report = FfmpegCheck.Evaluate(
            "9.0.2",
            new[]
            {
                Found("ffmpeg", Ffmpeg, "9.0.2-essentials_build-www.gyan.dev", true),
                new FfmpegToolStatus(
                    new FfmpegResolution("ffprobe", null, FfmpegSource.Missing),
                    null,
                    null,
                    null
                ),
            },
            Searched
        );

        Assert.False(report.Ok);
        Assert.False(report.Warning);
        Assert.StartsWith("Clips cannot be cut.", report.Detail);
        Assert.Contains("ffprobe: not found in " + Searched, report.Detail);
        Assert.Contains("heroesreplay deps install", report.Detail);
    }

    [Fact]
    public void Evaluate_AWorkingBuildOfAnotherVersion_PassesWithAWarning()
    {
        FfmpegCheckReport report = FfmpegCheck.Evaluate(
            "9.0.2",
            new[]
            {
                Found(
                    "ffmpeg",
                    @"C:\ffmpeg\bin\ffmpeg.exe",
                    "8.1.2-full_build-www.gyan.dev",
                    true,
                    FfmpegSource.Legacy
                ),
                Found("ffprobe", Ffprobe, "9.0.2-essentials_build-www.gyan.dev", null),
            },
            Searched
        );

        Assert.True(report.Ok);
        Assert.True(report.Warning);
        Assert.StartsWith("Not every tool is the pinned 9.0.2; clips still cut.", report.Detail);
        Assert.Contains("Not the pinned 9.0.2", report.Detail);
        Assert.Contains(@"C:\ffmpeg\bin\ffmpeg.exe (C:\ffmpeg\bin)", report.Detail);
    }

    [Fact]
    public void Evaluate_AToolThatDoesNotRun_Fails()
    {
        FfmpegCheckReport report = FfmpegCheck.Evaluate(
            "9.0.2",
            new[]
            {
                new FfmpegToolStatus(
                    new FfmpegResolution("ffmpeg", Ffmpeg, FfmpegSource.Installed),
                    null,
                    null,
                    "exit -1073741515: no error text"
                ),
            },
            Searched
        );

        Assert.False(report.Ok);
        Assert.Contains("did not report a version (exit -1073741515", report.Detail);
    }

    [Fact]
    public void Evaluate_AnFfmpegWithoutLibx264_Fails()
    {
        FfmpegCheckReport report = FfmpegCheck.Evaluate(
            "9.0.2",
            new[] { Found("ffmpeg", Ffmpeg, "9.0.2-lgpl", false, FfmpegSource.Path) },
            Searched
        );

        Assert.False(report.Ok);
        Assert.Contains("libx264", report.Detail);
    }

    [Theory]
    [InlineData(
        "ffmpeg version 9.0.2-essentials_build-www.gyan.dev Copyright (c) 2000-2026 the FFmpeg developers",
        "ffmpeg",
        "9.0.2-essentials_build-www.gyan.dev"
    )]
    [InlineData("ffprobe version n7.1-12-gabc Copyright", "ffprobe", "n7.1-12-gabc")]
    [InlineData("ffmpeg version 9.0.2", "ffmpeg", "9.0.2")]
    [InlineData("Usage: ffmpeg", "ffmpeg", null)]
    [InlineData("ffmpeg version", "ffmpeg", null)]
    [InlineData("", "ffmpeg", null)]
    public void ParseVersion_ReadsTheVersionWord(string line, string tool, string version)
    {
        Assert.Equal(version, FfmpegCheck.ParseVersion(line, tool));
    }

    [Theory]
    [InlineData("9.0.2", "9.0.2", true)]
    [InlineData("9.0.2-essentials_build-www.gyan.dev", "9.0.2", true)]
    [InlineData("9.0.2-full_build-www.gyan.dev", "9.0.2", true)]
    [InlineData("n9.0.2-3-g1234", "9.0.2", true)]
    [InlineData("9.0.21", "9.0.2", false)]
    [InlineData("9.0", "9.0.2", false)]
    [InlineData("2026-10-08-git-ec420ba161-essentials_build-www.gyan.dev", "9.0.2", false)]
    [InlineData(null, "9.0.2", false)]
    public void IsPinned_MatchesTheVersionNotItsPrefix(string version, string pinned, bool ok)
    {
        Assert.Equal(ok, FfmpegCheck.IsPinned(version, pinned));
    }

    [Fact]
    public void ListsEncoder_ReadsTheEncoderColumn()
    {
        const string encoders =
            "Encoders:\r\n V..... = Video\r\n ------\r\n V....D libx264              libx264 H.264 / AVC / MPEG-4 AVC (codec h264)\r\n A....D aac                  AAC (Advanced Audio Coding)\r\n";

        Assert.True(FfmpegCheck.ListsEncoder(encoders, "libx264"));
        Assert.True(FfmpegCheck.ListsEncoder(encoders, "aac"));
        Assert.False(FfmpegCheck.ListsEncoder(encoders, "libx265"));
        Assert.False(FfmpegCheck.ListsEncoder(null, "libx264"));
    }

    private static FfmpegToolStatus Found(
        string tool,
        string path,
        string version,
        bool? encodesH264,
        FfmpegSource source = FfmpegSource.Installed
    ) =>
        new(
            new FfmpegResolution(tool, path, source),
            tool + " version " + version,
            encodesH264,
            null
        );
}
