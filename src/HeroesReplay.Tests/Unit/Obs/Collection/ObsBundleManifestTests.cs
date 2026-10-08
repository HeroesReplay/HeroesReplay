using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.CLI.Commands.Obs;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.SelfUpdate;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

/// <summary>
/// <c>obs/bundle.manifest</c> schema 2 (#308): sizes, SHA-256, the collection hash, and the
/// scene and source contract, read by <c>update install-obs</c>, <c>obs validate</c>, and
/// <c>obs bundle</c>, with the plain list and no manifest still accepted.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsBundleManifestTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-obs-bundle-" + Path.GetRandomFileName()
    );

    public ObsBundleManifestTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void GeneratedManifest_MatchesTheRepoObsFolder()
    {
        string obs = FakeObs.RepoObsDirectory();
        IReadOnlyList<string> listed = ObsCollectionBundle.AssetPaths(obs);
        ObsContract contract = ObsContract.From(ReleaseObsSettings());

        ObsBundleManifest manifest = ObsCollectionBundle.Create(obs, listed, contract);

        Assert.Equal(ObsCollectionBundle.SchemaVersion, manifest.SchemaVersion);
        Assert.Equal("Default.json", manifest.Collection);
        Assert.Equal(
            listed.Order(StringComparer.Ordinal),
            manifest.Assets.Select(asset => asset.Path)
        );
        foreach (ObsBundleAsset asset in manifest.Assets)
        {
            string file = Path.Combine(obs, asset.Path.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(new FileInfo(file).Length, asset.Size);
            Assert.Matches("^[0-9A-F]{64}$", asset.Sha256);
        }

        string collectionHash = ObsCollectionBundle.Sha256(Path.Combine(obs, "Default.json"));
        Assert.Equal(collectionHash, manifest.CollectionSha256);
        Assert.Equal(
            collectionHash,
            manifest.Assets.Single(asset => asset.Path == "Default.json").Sha256
        );

        // The contract is ObsContract's names, with each source's kind from Default.json.
        Assert.Equal(contract.Scenes, manifest.Contract.Scenes);
        Assert.Contains("game-scene", manifest.Contract.Scenes);
        Assert.Contains("match-report", manifest.Contract.Scenes);
        Assert.Contains(
            new ObsBundleSource("current-replay", "text_gdiplus"),
            manifest.Contract.Sources
        );
        Assert.Contains(
            new ObsBundleSource("match-report-browser", "browser_source"),
            manifest.Contract.Sources
        );
        Assert.All(manifest.Contract.Sources, source => Assert.NotNull(source.Kind));
        Assert.Contains(
            new ObsContractItem("game-scene", "current-replay"),
            manifest.Contract.Items
        );

        ObsBundleCheck check = ObsCollectionBundle.Verify(obs, manifest);
        Assert.True(check.Ok, check.Describe());
        Assert.Equal(listed.Count, check.Files);

        // It survives the JSON round trip the release ships.
        string json = ObsCollectionBundle.Serialize(manifest);
        Assert.True(ObsCollectionBundle.IsVersioned(json));
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(
                collectionHash,
                document.RootElement.GetProperty("collectionSha256").GetString()
            );
        }

        ObsBundleManifest parsed = ObsCollectionBundle.Parse(json);
        Assert.Equal(manifest.Assets, parsed.Assets);
        Assert.Equal(manifest.Contract.Items, parsed.Contract.Items);
        Assert.True(ObsCollectionBundle.Verify(obs, parsed).Ok);
    }

    [Fact]
    public void OneChangedByte_IsBundleInvalidWithThatPath()
    {
        string obs = WriteBundle(Path.Combine(root, "app"));
        string bronze = Path.Combine(obs, "Ranks", "bronze.png");
        long size = new FileInfo(bronze).Length;
        Assert.True(ObsCollectionBundle.Verify(obs).Ok);

        FlipOneByte(bronze);

        ObsBundleCheck check = ObsCollectionBundle.Verify(obs);
        Assert.False(check.Ok);
        Assert.Equal(ObsBundleFormat.Versioned, check.Format);
        ObsBundleProblem problem = Assert.Single(check.Problems);
        Assert.Equal("Ranks/bronze.png", problem.Path);
        Assert.Contains("SHA-256", problem.Reason, StringComparison.Ordinal);
        Assert.Equal(size, new FileInfo(bronze).Length);
        Assert.StartsWith(ObsValidator.BundleInvalid, check.Describe(), StringComparison.Ordinal);
        Assert.Contains("obs/Ranks/bronze.png", check.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void ATruncatedOrMissingFile_IsBundleInvalid()
    {
        string obs = WriteBundle(Path.Combine(root, "app"));
        File.WriteAllBytes(
            Path.Combine(obs, "Ranks", "bronze.png"),
            File.ReadAllBytes(Path.Combine(obs, "Ranks", "bronze.png")).Take(10).ToArray()
        );
        File.Delete(Path.Combine(obs, "countdown", "index.html"));

        ObsBundleCheck check = ObsCollectionBundle.Verify(obs);

        Assert.Equal(
            ["Ranks/bronze.png", "countdown/index.html"],
            check.Problems.Select(problem => problem.Path).Order(StringComparer.Ordinal)
        );
        Assert.Contains(check.Problems, problem => problem.Reason.Contains("bytes"));
        Assert.Contains(check.Problems, problem => problem.Reason == "is missing");
    }

    [Fact]
    public void AContractNameTheCollectionLacks_IsBundleInvalid()
    {
        string obs = WriteBundle(Path.Combine(root, "app"));
        ObsBundleManifest manifest = ObsCollectionBundle.Parse(
            File.ReadAllText(Path.Combine(obs, ObsCollectionBundle.FileName))
        );
        ObsBundleManifest changed = manifest with
        {
            Contract = manifest.Contract with
            {
                Scenes = [.. manifest.Contract.Scenes, "intermission"],
                Sources =
                [
                    .. manifest.Contract.Sources.Select(source =>
                        source.Name == "match-report-browser"
                            ? source with
                            {
                                Kind = "image_source",
                            }
                            : source
                    ),
                ],
            },
        };

        ObsBundleCheck check = ObsCollectionBundle.Verify(obs, changed);

        Assert.Equal(2, check.Problems.Count);
        Assert.All(check.Problems, problem => Assert.Equal("Default.json", problem.Path));
        Assert.Contains(check.Problems, problem => problem.Reason.Contains("'intermission'"));
        Assert.Contains(check.Problems, problem => problem.Reason.Contains("image_source"));
    }

    [Fact]
    public void ANewerSchemaOrAPathOutsideObs_IsBundleInvalid()
    {
        string obs = WriteBundle(Path.Combine(root, "app"));
        ObsBundleManifest manifest = ObsCollectionBundle.Parse(
            File.ReadAllText(Path.Combine(obs, ObsCollectionBundle.FileName))
        );

        ObsBundleCheck newer = ObsCollectionBundle.Verify(obs, manifest with { SchemaVersion = 3 });
        ObsBundleCheck escape = ObsCollectionBundle.Verify(
            obs,
            manifest with
            {
                Assets = [.. manifest.Assets, new ObsBundleAsset("../appsettings.json", 1, "00")],
            }
        );

        Assert.Contains("schemaVersion 3", Assert.Single(newer.Problems).Reason);
        Assert.Equal("../appsettings.json", Assert.Single(escape.Problems).Path);
    }

    [Fact]
    public void PlainList_IsCheckedForPresenceOnly()
    {
        string obs = Path.Combine(root, "checkout", "obs");
        CopyRepoFile(obs, "Default.json");
        CopyRepoFile(obs, "Ranks/bronze.png");
        File.WriteAllText(
            Path.Combine(obs, ObsCollectionBundle.FileName),
            "# assets\nDefault.json\nRanks/bronze.png\n"
        );

        ObsBundleCheck present = ObsCollectionBundle.Verify(obs);
        File.Delete(Path.Combine(obs, "Ranks", "bronze.png"));
        ObsBundleCheck missing = ObsCollectionBundle.Verify(obs);

        Assert.True(present.Ok);
        Assert.Equal(ObsBundleFormat.PlainList, present.Format);
        Assert.Equal(2, present.Files);
        Assert.Contains("no checksums", present.Describe(), StringComparison.Ordinal);
        Assert.Equal("Ranks/bronze.png", Assert.Single(missing.Problems).Path);
    }

    [Fact]
    public void NoManifest_IsNotAProblem()
    {
        string obs = Path.Combine(root, "old", "obs");
        CopyRepoFile(obs, "Default.json");

        ObsBundleCheck check = ObsCollectionBundle.Verify(obs);

        Assert.True(check.Ok);
        Assert.Equal(ObsBundleFormat.Missing, check.Format);
        Assert.StartsWith("Warning:", check.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void InstallObs_MissingManifest_StillInstallsWithAWarning()
    {
        string install = Path.Combine(root, "app");
        CopyRepoFile(Path.Combine(install, "obs"), "Default.json");
        string appData = Path.Combine(root, "appdata");

        IReadOnlyList<string> notes = InstallObs(install, appData);

        Assert.Contains(
            notes,
            note =>
                note.StartsWith("OBS bundle: Warning:", StringComparison.Ordinal)
                && note.Contains("bundle.manifest is missing", StringComparison.Ordinal)
        );
        Assert.True(File.Exists(ObsNames.CollectionFile(appData, "HeroesReplay")));
    }

    [Fact]
    public void InstallObs_MatchingManifest_InstallsAndSaysSo()
    {
        string install = Path.Combine(root, "app");
        WriteBundle(install);
        string appData = Path.Combine(root, "appdata");

        IReadOnlyList<string> notes = InstallObs(install, appData);

        Assert.Contains(notes, note => note.Contains("match their size and SHA-256"));
        Assert.True(File.Exists(ObsNames.CollectionFile(appData, "HeroesReplay")));
    }

    [Fact]
    public void InstallObs_TamperedBundle_IsRefusedBeforeAnythingIsWritten()
    {
        string install = Path.Combine(root, "app");
        string obs = WriteBundle(install);
        FlipOneByte(Path.Combine(obs, "Ranks", "bronze.png"));
        string appData = Path.Combine(root, "appdata");

        ObsBundleInvalidException refused = Assert.Throws<ObsBundleInvalidException>(() =>
            InstallObs(install, appData)
        );

        Assert.Equal(ObsValidator.BundleInvalid, refused.Code);
        Assert.Contains("obs/Ranks/bronze.png", refused.Message, StringComparison.Ordinal);
        Assert.Contains("were not written", refused.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(appData));
        Assert.False(Directory.Exists(Path.Combine(root, "managed")));
    }

    [Fact]
    public void Validate_ATamperedInstall_HasBundleInvalidForThatFile()
    {
        string install = Path.Combine(root, "app");
        string obs = WriteBundle(install);
        FlipOneByte(Path.Combine(obs, "Ranks", "bronze.png"));
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);

        ObsValidation validation = Validate(install, data, obs);

        Assert.False(validation.Ok);
        ObsFinding finding = Assert.Single(
            validation.Findings,
            finding => finding.Code == ObsValidator.BundleInvalid
        );
        Assert.Equal(ObsValidator.Error, finding.Severity);
        Assert.Equal("Ranks/bronze.png", finding.Subject);
        Assert.Contains("Install the release again", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NoManifest_IsAnUnverifiedWarning()
    {
        string install = Path.Combine(root, "app");
        string obs = WriteBundle(install);
        File.Delete(Path.Combine(obs, ObsCollectionBundle.FileName));
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);

        ObsValidation validation = Validate(install, data, obs);

        ObsFinding finding = Assert.Single(
            validation.Findings,
            finding => finding.Code == ObsValidator.BundleUnverified
        );
        Assert.Equal(ObsValidator.Warning, finding.Severity);
        Assert.DoesNotContain(validation.Findings, f => f.Code == ObsValidator.BundleInvalid);
    }

    [Fact]
    public void Validate_TheRepoCheckout_HasNoBundleFinding()
    {
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);

        ObsValidation validation = ObsValidator.Validate(
            FakeObs.Installed(data).Open(null, null),
            FakeObs.InspectionSettings(data)
        );

        Assert.DoesNotContain(
            validation.Findings,
            finding => finding.Code.StartsWith("obs.bundle_", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Command_Write_TurnsThePublishedListIntoSchema2_AndCheckReadsIt()
    {
        string install = Path.Combine(root, "publish");
        string obs = Path.Combine(install, "obs");
        foreach (string asset in BundleAssets)
        {
            CopyRepoFile(obs, asset);
        }

        File.WriteAllLines(Path.Combine(obs, ObsCollectionBundle.FileName), BundleAssets);
        foreach (string settings in new[] { "appsettings.json", "appsettings.prod.json" })
        {
            File.Copy(
                Path.Combine(AppContext.BaseDirectory, settings),
                Path.Combine(install, settings)
            );
        }

        var output = new StringWriter();
        var error = new StringWriter();
        int written = ObsBundleCommand.Write(install, output, error);

        Assert.True(written == 0, error.ToString());
        Assert.Contains("schema 2", output.ToString(), StringComparison.Ordinal);
        string text = File.ReadAllText(Path.Combine(obs, ObsCollectionBundle.FileName));
        Assert.True(ObsCollectionBundle.IsVersioned(text));
        Assert.Contains(
            "game-scene",
            ObsCollectionBundle.Parse(text).Contract.Scenes,
            StringComparer.Ordinal
        );

        var json = new StringWriter();
        Assert.Equal(0, ObsBundleCommand.Check(install, json: true, json));
        using (JsonDocument document = JsonDocument.Parse(json.ToString()))
        {
            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("versioned", document.RootElement.GetProperty("format").GetString());
            Assert.Equal(BundleAssets.Length, document.RootElement.GetProperty("files").GetInt32());
        }

        // Writing again reads the schema 2 manifest's own paths.
        Assert.Equal(0, ObsBundleCommand.Write(install, new StringWriter(), new StringWriter()));

        FlipOneByte(Path.Combine(obs, "Ranks", "bronze.png"));
        var text2 = new StringWriter();
        Assert.Equal(1, ObsBundleCommand.Check(install, json: false, text2));
        Assert.Contains(
            "error obs.bundle_invalid Ranks/bronze.png",
            text2.ToString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Command_Write_RefusesASourceCheckout()
    {
        string checkout = Path.Combine(root, "checkout");
        CopyRepoFile(Path.Combine(checkout, "obs"), "Default.json");
        File.WriteAllText(
            Path.Combine(checkout, "obs", ObsCollectionBundle.FileName),
            "Default.json\n"
        );
        File.WriteAllText(Path.Combine(checkout, "heroes-replay.slnx"), "<Solution />");
        var error = new StringWriter();

        Assert.Equal(1, ObsBundleCommand.Write(checkout, new StringWriter(), error));
        Assert.Contains("source checkout", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            "Default.json\n",
            File.ReadAllText(Path.Combine(checkout, "obs", ObsCollectionBundle.FileName))
        );
    }

    private static readonly string[] BundleAssets =
    {
        "Default.json",
        "Default/basic.ini",
        "Ranks/bronze.png",
        "countdown/index.html",
    };

    /// <summary>A release-like obs folder under <paramref name="install"/> with a schema 2 manifest.</summary>
    private static string WriteBundle(string install)
    {
        string obs = Path.Combine(install, "obs");
        foreach (string asset in BundleAssets)
        {
            CopyRepoFile(obs, asset);
        }

        ObsBundleManifest manifest = ObsCollectionBundle.Create(
            obs,
            BundleAssets,
            ObsContract.From(FakeObs.Settings())
        );
        File.WriteAllText(
            Path.Combine(obs, ObsCollectionBundle.FileName),
            ObsCollectionBundle.Serialize(manifest)
        );
        return obs;
    }

    private static void CopyRepoFile(string obs, string relative)
    {
        string target = Path.Combine(obs, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        File.Copy(
            Path.Combine(
                FakeObs.RepoObsDirectory(),
                relative.Replace('/', Path.DirectorySeparatorChar)
            ),
            target
        );
    }

    /// <summary>Changes one byte in the middle of the file; the size stays the same.</summary>
    private static void FlipOneByte(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
    }

    private IReadOnlyList<string> InstallObs(string install, string appData) =>
        ReleaseInstall.InstallObsFiles(
            new ReleaseObsInstall
            {
                InstallDirectory = install,
                AppData = appData,
                ObsIsRunning = false,
                Managed = new ObsManagedFiles(Path.Combine(root, "managed")),
                DataDirectory = Path.Combine(root, "data"),
                ProfileName = "HeroesReplay",
                CollectionName = "HeroesReplay",
            }
        );

    private static ObsValidation Validate(string install, string data, string obs) =>
        ObsValidator.Validate(
            FakeObs.Installed(data, obs).Open(null, null),
            new ObsInspectionSettings(
                FakeObs.Settings(),
                install,
                data,
                false,
                Path.Combine(data, "stream-armed")
            )
        );

    /// <summary>The OBS names a release packages: appsettings.json and the prod overlay.</summary>
    private static OBSSettings ReleaseObsSettings() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.prod.json", optional: true)
            .Build()
            .GetSection("OBS")
            .Get<OBSSettings>();
}
