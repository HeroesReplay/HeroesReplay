using System;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs.Collection;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

/// <summary>#330: stable, verified copies of an install's OBS files.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ObsAssetStoreTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-assets-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private ObsAssetStore Store => new(Path.Combine(root, "managed", ObsAssetStore.FolderName));

    [Fact]
    public void Ensure_CopiesAReleaseBundle_CheckedAgainstTheManifestHashes()
    {
        string obs = StableAssetsFixture.Install(root, "app", versioned: true);

        ObsAssetCopy copy = Store.Ensure(obs, Now);

        Assert.True(copy.Ok, copy.Message);
        Assert.True(copy.Copied);
        Assert.Equal(Path.Combine(Store.Root, copy.BundleHash), copy.AssetRoot);
        Assert.Matches("^[0-9A-F]{16}$", copy.BundleHash);
        foreach (string file in StableAssetsFixture.Files)
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(obs, file)),
                File.ReadAllBytes(Path.Combine(copy.AssetRoot, file))
            );
        }

        Assert.Equal(
            Now,
            File.GetLastWriteTimeUtc(Path.Combine(copy.AssetRoot, ObsAssetStore.MarkerFileName))
        );
        Assert.Equal(copy.AssetRoot, Store.Planned(obs));
        // Nothing half-made is left beside it.
        Assert.Equal(
            [copy.BundleHash],
            Directory.GetDirectories(Store.Root).Select(Path.GetFileName)
        );

        ObsAssetCopy again = Store.Ensure(obs, Now.AddHours(1));
        Assert.True(again.Ok);
        Assert.False(again.Copied);
        Assert.Equal(copy.AssetRoot, again.AssetRoot);
    }

    [Fact]
    public void Ensure_HashesASourceBuildsPlainList_AndAChangedFileIsANewCopy()
    {
        string obs = StableAssetsFixture.Install(root, "checkout", versioned: false);

        ObsAssetCopy first = Store.Ensure(obs, Now);
        File.WriteAllText(Path.Combine(obs, "countdown", "index.html"), "<html>changed</html>");
        ObsAssetCopy second = Store.Ensure(obs, Now);

        Assert.True(first.Ok, first.Message);
        Assert.True(second.Ok, second.Message);
        Assert.NotEqual(first.BundleHash, second.BundleHash);
        Assert.Equal(
            "<html>changed</html>",
            File.ReadAllText(Path.Combine(second.AssetRoot, "countdown", "index.html"))
        );
        // The first copy is still whole: a collection may point at it.
        Assert.True(File.Exists(Path.Combine(first.AssetRoot, "countdown", "index.html")));
    }

    [Fact]
    public void Ensure_RefusesAFileThatDoesNotMatchTheManifest()
    {
        string obs = StableAssetsFixture.Install(root, "app", versioned: true);
        string gold = Path.Combine(obs, "Ranks", "gold.png");
        byte[] bytes = File.ReadAllBytes(gold);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(gold, bytes);

        ObsAssetCopy copy = Store.Ensure(obs, Now);

        Assert.False(copy.Ok);
        Assert.Null(copy.AssetRoot);
        Assert.Contains("Ranks/gold.png", copy.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(Store.Root));
    }

    [Fact]
    public void Ensure_WithoutAManifest_CopiesNothing()
    {
        string obs = StableAssetsFixture.Install(root, "old-release", versioned: true);
        File.Delete(Path.Combine(obs, ObsCollectionBundle.FileName));

        ObsAssetCopy copy = Store.Ensure(obs, Now);

        Assert.False(copy.Ok);
        Assert.Contains(ObsCollectionBundle.FileName, copy.Message, StringComparison.Ordinal);
        Assert.Null(Store.Planned(obs));
        Assert.False(Directory.Exists(Store.Root));
    }

    [Fact]
    public void Ensure_PutsBackAFileMissingFromAnExistingCopy()
    {
        string obs = StableAssetsFixture.Install(root, "app", versioned: true);
        ObsAssetCopy first = Store.Ensure(obs, Now);
        File.Delete(Path.Combine(first.AssetRoot, "Ranks", "gold.png"));

        ObsAssetCopy repaired = Store.Ensure(obs, Now);

        Assert.True(repaired.Ok, repaired.Message);
        Assert.True(repaired.Copied);
        Assert.Equal(first.AssetRoot, repaired.AssetRoot);
        Assert.True(File.Exists(Path.Combine(first.AssetRoot, "Ranks", "gold.png")));
    }

    [Fact]
    public void Prune_KeepsTheCopyInUse_OnesACollectionOrBackupNames_AndRecentOnes()
    {
        ObsAssetStore store = Store;
        string inUse = Copy(store, "1111111111111111", Now.AddDays(-30));
        string inLive = Copy(store, "2222222222222222", Now.AddDays(-30));
        string inBackup = Copy(store, "3333333333333333", Now.AddDays(-30));
        string recent = Copy(store, "4444444444444444", Now.AddDays(-1));
        string old = Copy(store, "5555555555555555", Now.AddDays(-3));
        string staging = Path.Combine(store.Root, ".staging-abc");
        Directory.CreateDirectory(staging);
        Directory.SetCreationTimeUtc(staging, Now.AddHours(-2));
        string live =
            "{\"settings\":{\"file\":\"" + inLive.Replace('\\', '/') + "/Ranks/gold.png\"}}";
        string backup =
            "{\"settings\":{\"file\":\""
            + inBackup.Replace(@"\", @"\\")
            + "\\\\Ranks\\\\gold.png\"}}";

        var deleted = store.Prune([live, backup], "1111111111111111", Now);

        Assert.True(Directory.Exists(inUse));
        Assert.True(Directory.Exists(inLive));
        Assert.True(Directory.Exists(inBackup));
        Assert.True(Directory.Exists(recent));
        Assert.False(Directory.Exists(old));
        Assert.False(Directory.Exists(staging));
        Assert.Equal([old], deleted);
    }

    /// <summary>A finished copy with a marker last used at <paramref name="lastUse"/>.</summary>
    private static string Copy(ObsAssetStore store, string hash, DateTime lastUse)
    {
        string folder = Path.Combine(store.Root, hash);
        Directory.CreateDirectory(Path.Combine(folder, "Ranks"));
        File.WriteAllText(Path.Combine(folder, "Ranks", "gold.png"), "png");
        string marker = Path.Combine(folder, ObsAssetStore.MarkerFileName);
        File.WriteAllText(marker, "Ranks/gold.png\t3\tX\n");
        File.SetLastWriteTimeUtc(marker, lastUse);
        return folder;
    }
}
