using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientBuildArchiveTests : IDisposable
{
    private const string Floor = "2.57.0.98285";
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-clients-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("2.57.0.98285", true)]
    [InlineData("2.57.0.98284", true)]
    [InlineData("2.57.0.98304", true)]
    [InlineData("2.57.1.1", true)]
    [InlineData("2.58.0.1", true)]
    [InlineData("2.55.17.98025", false)]
    [InlineData("2.56.99.99999", false)]
    [InlineData("", false)]
    public void ShouldKeep_CoversThePatchLineAndNewerBuilds(string version, bool keep)
    {
        Assert.Equal(keep, ClientBuildArchive.ShouldKeep(version, Floor));
    }

    [Fact]
    public void ShouldKeep_WithoutAFloorKeepsEveryBuild()
    {
        Assert.True(ClientBuildArchive.ShouldKeep("2.55.17.98025", null));
    }

    [Theory]
    [InlineData("2.57.0.98304", "Base98304")]
    [InlineData("2, 57, 0, 98285", "Base98285")]
    [InlineData("not-a-build", null)]
    [InlineData("", null)]
    public void BaseDirectoryName_UsesTheBuildNumber(string version, string folder)
    {
        Assert.Equal(folder, ClientBuildArchive.BaseDirectoryName(version));
    }

    [Fact]
    public void ResolveDirectory_UsesTheConfiguredFolderThenDataClients()
    {
        Assert.Equal(
            @"D:\kept",
            ClientBuildArchive.ResolveDirectory(@"D:\kept", @"C:\heroesreplay\Data")
        );
        Assert.Equal(
            @"C:\heroesreplay\Data\Clients",
            ClientBuildArchive.ResolveDirectory(" ", @"C:\heroesreplay\Data")
        );
        Assert.Null(ClientBuildArchive.ResolveDirectory(null, null));
    }

    [Fact]
    public void Preserve_CopiesEveryIterationOnTheLineAndLeavesOlderPatches()
    {
        InstalledClient older = Write("live", "2.55.17.98025", "old-client");
        InstalledClient first = Write("live", "2.57.0.98285", "build-98285");
        InstalledClient next = Write("live", "2.57.0.98304", "build-98304");
        string archive = Path.Combine(root, "archive");

        IReadOnlyList<ClientBuildKeepItem> kept = ClientBuildArchive.Preserve(
            new[] { older, first, next },
            archive,
            Floor
        );

        Assert.Equal(2, kept.Count);
        Assert.All(kept, item => Assert.Equal(ClientBuildKeep.Copied, item.Result));
        Assert.Equal(
            new[] { "2.57.0.98285", "2.57.0.98304" },
            kept.Select(item => item.Version).ToArray()
        );
        Assert.Equal("build-98285", File.ReadAllText(Archived(archive, "2.57.0.98285")));
        Assert.Equal("build-98304", File.ReadAllText(Archived(archive, "2.57.0.98304")));
        Assert.False(File.Exists(Archived(archive, "2.55.17.98025")));
    }

    [Fact]
    public void Preserve_DoesNotReplaceACopyAlreadyKept()
    {
        InstalledClient client = Write("live", "2.57.0.98304", "first-bytes");
        string archive = Path.Combine(root, "archive");
        ClientBuildArchive.Preserve(new[] { client }, archive, Floor);
        File.WriteAllText(client.ExePath, "replaced-source-bytes");

        IReadOnlyList<ClientBuildKeepItem> again = ClientBuildArchive.Preserve(
            new[] { client },
            archive,
            Floor
        );

        Assert.Equal(ClientBuildKeep.AlreadyKept, again[0].Result);
        Assert.Equal("first-bytes", File.ReadAllText(Archived(archive, "2.57.0.98304")));
    }

    [Fact]
    public void Preserve_SkipsAMissingExe()
    {
        var missing = new InstalledClient(
            "2.57.0.98304",
            Path.Combine(root, "gone", InstalledClientCatalog.ExeFileName)
        );

        IReadOnlyList<ClientBuildKeepItem> kept = ClientBuildArchive.Preserve(
            new[] { missing },
            Path.Combine(root, "archive"),
            Floor
        );

        Assert.Equal(ClientBuildKeep.Skipped, kept[0].Result);
    }

    [Fact]
    public void Preserve_WithNoArchiveDirectoryDoesNothing()
    {
        InstalledClient client = Write("live", "2.57.0.98304", "build-98304");
        Assert.Empty(ClientBuildArchive.Preserve(new[] { client }, " ", Floor));
    }

    [Fact]
    public void Restore_PutsAMissingIterationBackAndLeavesALiveExeAlone()
    {
        string archive = Path.Combine(root, "archive");
        InstalledClient kept = Write("source", "2.57.0.98285", "kept-98285");
        ClientBuildArchive.Preserve(new[] { kept }, archive, Floor);
        string game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(game, "Versions", "Base98285"));
        string live = Path.Combine(
            game,
            "Versions",
            "Base98304",
            InstalledClientCatalog.ExeFileName
        );
        Directory.CreateDirectory(Path.GetDirectoryName(live));
        File.WriteAllText(live, "live-98304");
        InstalledClient current = new InstalledClient("2.57.0.98304", live);
        ClientBuildArchive.Preserve(new[] { current }, archive, Floor);

        Assert.True(ClientBuildArchive.Restore(game, archive, "2.57.0.98285"));
        Assert.False(ClientBuildArchive.Restore(game, archive, "2.57.0.98304"));
        Assert.Equal(
            "kept-98285",
            File.ReadAllText(
                Path.Combine(game, "Versions", "Base98285", InstalledClientCatalog.ExeFileName)
            )
        );
        Assert.Equal("live-98304", File.ReadAllText(live));
    }

    [Fact]
    public void Restore_LeavesTheGameAloneWhenTheCopyWasNeverKept()
    {
        string game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(game, "Versions", "Base98285"));

        Assert.False(
            ClientBuildArchive.Restore(game, Path.Combine(root, "archive"), "2.57.0.98285")
        );
        Assert.False(ClientBuildArchive.Restore(game, Path.Combine(root, "archive"), "beta"));
        Assert.Empty(InstalledClientCatalog.FileVersions(game));
    }

    private InstalledClient Write(string area, string version, string payload)
    {
        string path = Path.Combine(
            root,
            area,
            ClientBuildArchive.BaseDirectoryName(version),
            InstalledClientCatalog.ExeFileName
        );
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, payload);
        return new InstalledClient(version, path);
    }

    private static string Archived(string archive, string version)
    {
        return Path.Combine(
            archive,
            ClientBuildArchive.BaseDirectoryName(version),
            InstalledClientCatalog.ExeFileName
        );
    }
}
