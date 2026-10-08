using System.IO;
using HeroesReplay.Core.Dependencies;
using Xunit;

namespace HeroesReplay.Tests.Unit.Dependencies;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class DependencyManifestTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void TheEmbeddedManifest_PinsFfmpegAndFfprobeFromAGitHubRelease()
    {
        DependencyPin ffmpeg = DependencyManifest.Ffmpeg;

        Assert.Equal("ffmpeg", ffmpeg.Name);
        Assert.Equal(new[] { "ffmpeg.exe", "ffprobe.exe" }, ffmpeg.Files);
        Assert.StartsWith("https://github.com/", ffmpeg.Url);
        Assert.Contains("/releases/download/" + ffmpeg.Version + "/", ffmpeg.Url);
        Assert.Matches("^[0-9a-f]{64}$", ffmpeg.Sha256);
        Assert.True(ffmpeg.Size > 0);
    }

    [Fact]
    public void Parse_ReadsATool()
    {
        DependencyPin pin = Assert.Single(DependencyManifest.Parse(Manifest()));

        Assert.Equal("9.0.2", pin.Version);
        Assert.Equal(new string('a', 64), pin.Sha256);
        Assert.Equal(1234, pin.Size);
    }

    [Fact]
    public void Parse_LowerCasesTheHash()
    {
        DependencyPin pin = Assert.Single(
            DependencyManifest.Parse(Manifest(sha256: new string('A', 64)))
        );

        Assert.Equal(new string('a', 64), pin.Sha256);
    }

    [Theory]
    [InlineData("http://example.test/ffmpeg.zip", Hash, "ffmpeg.exe")]
    [InlineData("https://example.test/ffmpeg.zip", "not-hex", "ffmpeg.exe")]
    [InlineData("https://example.test/ffmpeg.zip", Hash, "bin/ffmpeg.exe")]
    public void Parse_RejectsAnIncompletePin(string url, string sha256, string file)
    {
        Assert.Throws<InvalidDataException>(() =>
            DependencyManifest.Parse(Manifest(url: url, sha256: sha256, file: file))
        );
    }

    private static string Manifest(
        string url = "https://example.test/ffmpeg.zip",
        string sha256 = null,
        string file = "ffmpeg.exe"
    ) =>
        "{\"tools\":[{\"name\":\"ffmpeg\",\"version\":\"9.0.2\",\"url\":\""
        + url
        + "\",\"size\":1234,\"sha256\":\""
        + (sha256 ?? Hash)
        + "\",\"files\":[\""
        + file
        + "\",\"ffprobe.exe\"]}]}";
}
