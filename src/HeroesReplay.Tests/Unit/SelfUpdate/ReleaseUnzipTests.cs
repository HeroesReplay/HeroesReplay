using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using HeroesReplay.Core.SelfUpdate;
using Xunit;

namespace HeroesReplay.Tests.Unit.SelfUpdate;

/// <summary>
/// #450: the release unzip threw ZLibException over DllNotFoundException 24 times on the stream
/// PC after a .NET update deleted the runtime folder it loaded.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReleaseUnzipTests
{
    [Fact]
    public void TheIncidentsZLibException_IsARuntimeLoadFailure()
    {
        Exception zlib = ZLibException(
            "The underlying compression routine could not be loaded correctly.",
            new DllNotFoundException(
                "Unable to load DLL 'System.IO.Compression.Native' or one of its dependencies."
            )
        );

        Assert.True(ReleaseUnzip.IsRuntimeLoadFailure(zlib));
        Assert.True(ReleaseUnzip.IsRuntimeLoadFailure(ZLibException("zlib", inner: null)));
        Assert.True(
            ReleaseUnzip.IsRuntimeLoadFailure(new InvalidOperationException("staging", zlib))
        );
        Assert.True(
            ReleaseUnzip.IsRuntimeLoadFailure(
                new AggregateException(new IOException("a"), new DllNotFoundException("b"))
            )
        );
        Assert.Contains(
            "DllNotFoundException: Unable to load DLL 'System.IO.Compression.Native'",
            ReleaseUnzip.Cause(zlib),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void OtherStagingFailures_AreNot()
    {
        Assert.False(ReleaseUnzip.IsRuntimeLoadFailure(null));
        Assert.False(ReleaseUnzip.IsRuntimeLoadFailure(new IOException("disk full")));
        Assert.False(ReleaseUnzip.IsRuntimeLoadFailure(new HttpRequestException("503")));
        Assert.False(ReleaseUnzip.IsRuntimeLoadFailure(new InvalidDataException("bad zip")));
        Assert.False(
            ReleaseUnzip.IsRuntimeLoadFailure(new AggregateException(new IOException("a")))
        );
    }

    [Fact]
    public void AnUnzipThatCannotLoadZlib_IsDoneAgainInAChildProcess()
    {
        var steps = new List<string>();
        string folder = Path.Combine(
            Path.GetTempPath(),
            "hr-unzip-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            bool child = ReleaseUnzip.Extract(
                "release.zip",
                folder,
                error => steps.Add("serviced: " + error.GetType().Name),
                inProcess: (_, to) =>
                {
                    // A partial unzip is removed before the child process starts.
                    Directory.CreateDirectory(to);
                    steps.Add("in process");
                    throw ZLibException("zlib", new DllNotFoundException("zlib"));
                },
                childProcess: (zip, to) =>
                {
                    Assert.False(Directory.Exists(to));
                    steps.Add($"child {zip}");
                }
            );

            Assert.True(child);
            Assert.Equal(
                new[] { "in process", "serviced: ZLibException", "child release.zip" },
                steps.ToArray()
            );
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void AnUnzipThatWorks_OrFailsForAnotherReason_StaysInThisProcess()
    {
        bool childRan = false;
        Assert.False(
            ReleaseUnzip.Extract(
                "release.zip",
                "out",
                _ => Assert.Fail("not serviced"),
                inProcess: (_, _) => { },
                childProcess: (_, _) => childRan = true
            )
        );
        Assert.Throws<InvalidDataException>(() =>
            ReleaseUnzip.Extract(
                "release.zip",
                "out",
                _ => Assert.Fail("not serviced"),
                inProcess: (_, _) => throw new InvalidDataException("bad zip"),
                childProcess: (_, _) => childRan = true
            )
        );
        Assert.False(childRan);
    }

    [Fact]
    public void TarUnzipsAReleaseShapedZip()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-unzip-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = Path.Combine(root, "source");
            Directory.CreateDirectory(Path.Combine(source, "obs", "assets"));
            File.WriteAllText(Path.Combine(source, "heroesreplay.exe"), "exe");
            File.WriteAllText(Path.Combine(source, "obs", "assets", "waiting.png"), "png");
            string zip = Path.Combine(root, "heroesreplay-win-x64.zip");
            ZipFile.CreateFromDirectory(source, zip);
            string extracted = Path.Combine(root, "extract");

            Assert.True(
                ReleaseUnzip.Extract(
                    zip,
                    extracted,
                    _ => { },
                    inProcess: (_, _) =>
                        throw new DllNotFoundException("System.IO.Compression.Native")
                )
            );

            Assert.Equal("exe", File.ReadAllText(Path.Combine(extracted, "heroesreplay.exe")));
            Assert.Equal(
                "png",
                File.ReadAllText(Path.Combine(extracted, "obs", "assets", "waiting.png"))
            );
            Assert.Equal(
                new[] { "-xf", zip, "-C", extracted },
                ReleaseUnzip.TarStart(zip, extracted).ArgumentList
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>The runtime's own ZLibException, which is internal.</summary>
    private static Exception ZLibException(string message, Exception inner)
    {
        Type type = typeof(ZipArchive).Assembly.GetType(ReleaseUnzip.ZLibExceptionType, true);
        return (Exception)
            Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                new object[] { message, inner },
                culture: null
            );
    }
}
