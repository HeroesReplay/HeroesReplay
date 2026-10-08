using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Dependencies;
using Xunit;

namespace HeroesReplay.Tests.Unit.Dependencies;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class DependencyInstallerTests : IDisposable
{
    private const string Url = "https://example.test/ffmpeg-9.0.2-essentials_build.zip";
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-deps-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Install_ChecksTheHashAndExtractsOnlyThePinnedFiles()
    {
        byte[] archive = Archive(("ffmpeg.exe", "new ffmpeg"), ("ffprobe.exe", "new ffprobe"));
        var handler = new ArchiveHandler(archive);

        DependencyInstallResult result = await Installer(handler)
            .InstallAsync(Pin(archive), root, CancellationToken.None);

        Assert.Equal(DependencyInstallOutcome.Installed, result.Outcome);
        string target = Path.Combine(root, "ffmpeg");
        Assert.Equal(target, result.Directory);
        Assert.Equal("new ffmpeg", File.ReadAllText(Path.Combine(target, "ffmpeg.exe")));
        Assert.Equal("new ffprobe", File.ReadAllText(Path.Combine(target, "ffprobe.exe")));
        Assert.Equal(
            new[] { "ffmpeg.exe", "ffprobe.exe", DependencyInstallRecord.FileName },
            Directory.GetFileSystemEntries(target).Select(Path.GetFileName).Order().ToArray()
        );
        Assert.False(Directory.Exists(Path.Combine(root, DependencyInstaller.StagingFolder)));
        DependencyInstallRecord record = DependencyInstallRecord.Read(target);
        Assert.Equal("9.0.2", record.Version);
        Assert.Equal(Sha256(archive), record.Sha256);
        Assert.Equal(
            Sha256(Encoding.UTF8.GetBytes("new ffmpeg")),
            record.FindFile("ffmpeg.exe").Sha256
        );
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Install_WhenThePinnedBuildIsThere_DoesNotDownloadAgain()
    {
        byte[] archive = Archive(("ffmpeg.exe", "ffmpeg"), ("ffprobe.exe", "ffprobe"));
        var handler = new ArchiveHandler(archive);
        DependencyInstaller installer = Installer(handler);
        await installer.InstallAsync(Pin(archive), root, CancellationToken.None);

        DependencyInstallResult again = await installer.InstallAsync(
            Pin(archive),
            root,
            CancellationToken.None
        );

        Assert.Equal(DependencyInstallOutcome.AlreadyInstalled, again.Outcome);
        Assert.True(again.Ok);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Install_WithAWrongHash_InstallsNothingAndKeepsTheOldFiles()
    {
        string target = Path.Combine(root, "ffmpeg");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "ffmpeg.exe"), "old ffmpeg");
        byte[] archive = Archive(("ffmpeg.exe", "tampered"), ("ffprobe.exe", "tampered"));

        DependencyInstallResult result = await Installer(new ArchiveHandler(archive))
            .InstallAsync(
                Pin(archive) with
                {
                    Sha256 = new string('0', 64),
                },
                root,
                CancellationToken.None
            );

        Assert.Equal(DependencyInstallOutcome.Failed, result.Outcome);
        Assert.Contains("SHA-256 mismatch", result.Message);
        Assert.Equal("old ffmpeg", File.ReadAllText(Path.Combine(target, "ffmpeg.exe")));
        Assert.False(File.Exists(Path.Combine(target, "ffprobe.exe")));
        Assert.False(File.Exists(DependencyInstallRecord.PathIn(target)));
        Assert.False(Directory.Exists(Path.Combine(root, DependencyInstaller.StagingFolder)));
    }

    [Fact]
    public async Task Install_WithAWrongSize_InstallsNothing()
    {
        byte[] archive = Archive(("ffmpeg.exe", "ffmpeg"), ("ffprobe.exe", "ffprobe"));

        DependencyInstallResult result = await Installer(new ArchiveHandler(archive))
            .InstallAsync(
                Pin(archive) with
                {
                    Size = archive.Length + 1,
                },
                root,
                CancellationToken.None
            );

        Assert.Equal(DependencyInstallOutcome.Failed, result.Outcome);
        Assert.False(File.Exists(Path.Combine(root, "ffmpeg", "ffmpeg.exe")));
    }

    [Fact]
    public async Task Install_WhenTheArchiveLacksAPinnedFile_InstallsNothing()
    {
        byte[] archive = Archive(("ffmpeg.exe", "ffmpeg"));

        DependencyInstallResult result = await Installer(new ArchiveHandler(archive))
            .InstallAsync(Pin(archive), root, CancellationToken.None);

        Assert.Equal(DependencyInstallOutcome.Failed, result.Outcome);
        Assert.Contains("ffprobe.exe", result.Message);
        Assert.False(File.Exists(Path.Combine(root, "ffmpeg", "ffmpeg.exe")));
        Assert.False(Directory.Exists(Path.Combine(root, DependencyInstaller.StagingFolder)));
    }

    [Fact]
    public async Task Install_WhenTheDownloadFails_ReportsIt()
    {
        byte[] archive = Archive(("ffmpeg.exe", "ffmpeg"), ("ffprobe.exe", "ffprobe"));

        DependencyInstallResult result = await Installer(
                new ArchiveHandler(archive, HttpStatusCode.NotFound)
            )
            .InstallAsync(Pin(archive), root, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("404", result.Message);
    }

    [Fact]
    public async Task Install_ANewPin_ReplacesTheOldBuild()
    {
        byte[] first = Archive(("ffmpeg.exe", "9.0.2"), ("ffprobe.exe", "9.0.2"));
        await Installer(new ArchiveHandler(first))
            .InstallAsync(Pin(first), root, CancellationToken.None);
        byte[] second = Archive(("ffmpeg.exe", "9.1"), ("ffprobe.exe", "9.1"));

        DependencyInstallResult result = await Installer(new ArchiveHandler(second))
            .InstallAsync(Pin(second) with { Version = "9.1" }, root, CancellationToken.None);

        Assert.Equal(DependencyInstallOutcome.Installed, result.Outcome);
        Assert.Equal("9.1", File.ReadAllText(Path.Combine(root, "ffmpeg", "ffprobe.exe")));
        Assert.Equal("9.1", DependencyInstallRecord.Read(Path.Combine(root, "ffmpeg")).Version);
    }

    [Theory]
    [InlineData("ABCDEF", "abcdef", true)]
    [InlineData(" abcdef ", "abcdef", true)]
    [InlineData("abcdef", "abcdee", false)]
    [InlineData("", "abcdef", false)]
    [InlineData(null, null, false)]
    public void HashMatches_IgnoresCaseOnly(string actual, string expected, bool matches)
    {
        Assert.Equal(matches, DependencyInstaller.HashMatches(actual, expected));
    }

    [Fact]
    public async Task Sha256_IsLowerCaseHex()
    {
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "abc.txt");
        File.WriteAllText(file, "abc");

        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            await DependencyInstaller.Sha256Async(file, CancellationToken.None)
        );
    }

    private static DependencyInstaller Installer(HttpMessageHandler handler) =>
        new(new HttpClient(handler), retryDelay: TimeSpan.Zero);

    private static DependencyPin Pin(byte[] archive) =>
        new(
            "ffmpeg",
            "9.0.2",
            "test",
            Url,
            archive.Length,
            Sha256(archive),
            new[] { "ffmpeg.exe", "ffprobe.exe" }
        );

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>A Gyan-shaped archive: the exes under bin, plus files deps install must skip.</summary>
    private static byte[] Archive(params (string Name, string Text)[] exes)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("ffmpeg-9.0.2-essentials_build/bin/");
            foreach ((string name, string text) in exes)
            {
                Write(zip, "ffmpeg-9.0.2-essentials_build/bin/" + name, text);
            }

            Write(zip, "ffmpeg-9.0.2-essentials_build/bin/ffplay.exe", "ffplay");
            Write(zip, "ffmpeg-9.0.2-essentials_build/doc/ffmpeg.html", "doc");
        }

        return buffer.ToArray();
    }

    private static void Write(ZipArchive zip, string path, string text)
    {
        using StreamWriter writer = new(zip.CreateEntry(path).Open());
        writer.Write(text);
    }

    private sealed class ArchiveHandler(byte[] archive, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests++;
            Assert.Equal(Url, request.RequestUri?.ToString());
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new ByteArrayContent(archive) }
            );
        }
    }
}
