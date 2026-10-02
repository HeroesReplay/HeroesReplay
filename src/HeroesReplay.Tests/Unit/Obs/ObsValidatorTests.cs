using System;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsValidatorTests : IDisposable
{
    private readonly string data = Path.Combine(
        Path.GetTempPath(),
        "hr-obs-validate-" + Path.GetRandomFileName()
    );

    public ObsValidatorTests()
    {
        Directory.CreateDirectory(data);
    }

    public void Dispose()
    {
        Directory.Delete(data, recursive: true);
    }

    [Fact]
    public void ManagedCollection_PassesWithOnlyRuntimeFilesMissing()
    {
        ObsValidation validation = Validate(FakeObs.Installed(data));

        Assert.True(validation.Ok, Describe(validation));
        Assert.Null(validation.Code);
        Assert.Equal(0, validation.Errors);
        Assert.Equal(FakeObs.RepoObsDirectory(), validation.AssetRoot);
        Assert.All(
            validation.Findings,
            finding => Assert.Equal(ObsValidator.RuntimeFileMissing, finding.Code)
        );
        Assert.Contains(validation.Findings, finding => finding.Subject == "current-replay.file");
    }

    [Fact]
    public void ManagedCollection_WithTheRuntimeFiles_HasNoFindings()
    {
        File.WriteAllText(Path.Combine(data, "OBS.txt"), "replay");
        File.WriteAllText(Path.Combine(data, "prediction-report.html"), "<html></html>");
        File.WriteAllText(Path.Combine(data, "queue.html"), "<html></html>");

        ObsValidation validation = Validate(FakeObs.Installed(data));

        Assert.True(validation.Ok, Describe(validation));
        Assert.Empty(validation.Findings);
    }

    [Fact]
    public void UnmutedMicrophone_IsAnError()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Mic = "Mic/Aux";

        ObsValidation validation = Validate(obs);

        Assert.False(validation.Ok);
        Assert.Equal(ObsValidator.MicEnabled, validation.Code);
        ObsFinding finding = Single(validation, ObsValidator.MicEnabled);
        Assert.Equal(ObsValidator.Error, finding.Severity);
        Assert.Equal("Mic/Aux", finding.Subject);
        Assert.Contains("Disabled", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MutedMicrophone_IsAWarning()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Mic = "Mic/Aux";
        obs.MicMuted = true;

        ObsValidation validation = Validate(obs);

        Assert.True(validation.Ok, Describe(validation));
        Assert.Equal(ObsValidator.Warning, Single(validation, ObsValidator.MicMuted).Severity);
        Assert.DoesNotContain(validation.Findings, f => f.Code == ObsValidator.MicEnabled);
    }

    [Fact]
    public void WrongProfileAndCollection_AreTwoFindings()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Profile = "Untitled";
        obs.Collection = "Scenes";

        ObsValidation validation = Validate(obs);

        Assert.False(validation.Ok);
        Assert.Contains("'Untitled'", Single(validation, ObsSelection.ProfileMismatch).Message);
        Assert.Contains("'Scenes'", Single(validation, ObsSelection.CollectionMismatch).Message);
    }

    [Fact]
    public void MissingSceneAndSource_AreReportedByName()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.RemoveSource("request-queue");
        obs.RemoveSource("rank-points");

        ObsValidation validation = Validate(obs);

        Assert.False(validation.Ok);
        Assert.Equal("request-queue", Single(validation, ObsValidator.SceneMissing).Subject);
        Assert.Equal("rank-points", Single(validation, ObsValidator.SourceMissing).Subject);
        Assert.Equal(ObsNames.Default, Single(validation, ObsValidator.CollectionCustom).Subject);
    }

    [Fact]
    public void ASourceOfAnotherKind_IsAMismatch()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Source("match-report-browser")["id"] = "image_source";
        obs.Source("match-report-browser")["versioned_id"] = "image_source";

        ObsFinding finding = Single(Validate(obs), ObsValidator.SourceKindMismatch);

        Assert.Equal("match-report-browser", finding.Subject);
        Assert.Contains("browser_source", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASourceOutsideItsScene_IsAMissingSceneItem()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.RemoveSceneItem("game-scene", "current-replay");

        Assert.Equal(
            "game-scene/current-replay",
            Single(Validate(obs), ObsValidator.SceneItemMissing).Subject
        );
    }

    [Fact]
    public void UnrewrittenCollection_HasMissingFilesThisInstallCanFix()
    {
        ObsValidation validation = Validate(FakeObs.Packaged());

        Assert.False(validation.Ok);
        ObsFinding bronze = validation.Findings.Single(finding =>
            finding.Subject == "bronze-image.file"
        );
        Assert.Equal(ObsValidator.FileMissing, bronze.Code);
        Assert.Contains("Ranks", bronze.Message, StringComparison.Ordinal);
        Assert.Contains("which exists", bronze.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACheckoutPath_UnderAReleaseInstall_IsStale()
    {
        const string checkout = @"C:\heroesreplay\HeroesReplay\obs\Ranks\bronze.png";

        ObsFinding finding = ObsValidator.CheckReference(
            "bronze-image",
            "file",
            "C:/heroesreplay/HeroesReplay/obs/Ranks/bronze.png",
            @"C:\heroesreplay\app\obs",
            @"C:\heroesreplay\Data",
            path => path == checkout
        );

        Assert.Equal(ObsValidator.PathStale, finding.Code);
        Assert.Equal(ObsValidator.Warning, finding.Severity);
        Assert.Equal("bronze-image.file", finding.Subject);
        Assert.Contains(@"C:\heroesreplay\app\obs\Ranks\bronze.png", finding.Message);
    }

    [Fact]
    public void AMissingDataFile_IsARuntimeWarning_AndAMissingAssetAnError()
    {
        ObsFinding runtime = ObsValidator.CheckReference(
            "prediction-report-browser",
            "url",
            "file:///C:/heroesreplay/Data/prediction-report.html",
            @"C:\heroesreplay\app\obs",
            @"C:\heroesreplay\Data",
            _ => false
        );
        ObsFinding asset = ObsValidator.CheckReference(
            "hots-logo",
            "file",
            "C:/heroesreplay/app/obs/hots-logo.png",
            @"C:\heroesreplay\app\obs",
            @"C:\heroesreplay\Data",
            _ => false
        );

        Assert.Equal(ObsValidator.RuntimeFileMissing, runtime.Code);
        Assert.Equal(ObsValidator.Warning, runtime.Severity);
        Assert.Equal(ObsValidator.FileMissing, asset.Code);
        Assert.Equal(ObsValidator.Error, asset.Severity);
    }

    [Fact]
    public void AWebUrl_IsNotEchoed()
    {
        Assert.Null(
            ObsValidator.CheckReference(
                "overlay",
                "url",
                "https://overlay.example/widget?token=SECRET",
                null,
                null,
                _ => false
            )
        );
        ObsFinding broken = ObsValidator.CheckReference(
            "overlay",
            "url",
            "https://?token=SECRET",
            null,
            null,
            _ => false
        );

        Assert.Equal(ObsValidator.UrlInvalid, broken.Code);
        Assert.DoesNotContain("SECRET", broken.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABrokenWebUrl_IsInvalid()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Source("soundcloud")["settings"]["url"] = "https://";

        Assert.Equal("soundcloud.url", Single(Validate(obs), ObsValidator.UrlInvalid).Subject);
    }

    [Fact]
    public void AMissingRequest_IsACompatibilityError()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.MissingRequests.Add("SetRecordDirectory");

        ObsFinding finding = Single(Validate(obs), ObsValidator.RequestUnavailable);

        Assert.Equal("SetRecordDirectory", finding.Subject);
        Assert.Contains("5.6.3", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindingsAreErrorsFirst()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Mic = "Mic/Aux";

        ObsValidation validation = Validate(obs);

        Assert.Equal(ObsValidator.Error, validation.Findings[0].Severity);
        Assert.Equal(1, validation.Errors);
        Assert.Equal(validation.Findings.Count - 1, validation.Warnings);
    }

    [Theory]
    [InlineData("file:///C:/heroes%20replay/a.html?x=1#top", @"C:\heroes replay\a.html")]
    [InlineData("C:/heroesreplay/Data/OBS.txt", @"C:\heroesreplay\Data\OBS.txt")]
    [InlineData("Ranks/bronze.png", @"Ranks\bronze.png")]
    public void LocalPath_DropsTheUrlParts(string value, string expected)
    {
        Assert.Equal(expected, ObsValidator.LocalPath(value));
    }

    private ObsValidation Validate(FakeObs obs) =>
        ObsValidator.Validate(obs.Open(null, null), FakeObs.InspectionSettings(data));

    private static ObsFinding Single(ObsValidation validation, string code) =>
        Assert.Single(validation.Findings, finding => finding.Code == code);

    private static string Describe(ObsValidation validation) =>
        string.Join(
            Environment.NewLine,
            validation.Findings.Select(finding =>
                finding.Severity
                + " "
                + finding.Code
                + " "
                + finding.Subject
                + ": "
                + finding.Message
            )
        );
}
