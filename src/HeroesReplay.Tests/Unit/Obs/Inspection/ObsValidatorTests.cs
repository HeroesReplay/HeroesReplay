using System;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Inspection;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Inspection;

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

    [Fact]
    public void RequiredRequests_AreTheOnesTheSpectatorSends()
    {
        Assert.Contains("SetSourceFilterEnabled", ObsValidator.RequiredRequests);
        Assert.DoesNotContain("SetSourceFilterSettings", ObsValidator.RequiredRequests);
        Assert.Contains("SetRecordDirectory", ObsValidator.RequiredRequests);
    }

    [Fact]
    public void ACanvasOtherThan1080p_IsAnError()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Video["baseWidth"] = 1280;
        obs.Video["baseHeight"] = 720;

        ObsValidation validation = Validate(obs);
        ObsFinding finding = Single(validation, ObsValidator.CanvasMismatch);

        Assert.False(validation.Ok);
        Assert.Equal(ObsValidator.Error, finding.Severity);
        Assert.Equal("1280x720", finding.Subject);
        Assert.Contains("1920x1080", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AScaledOutput_IsTheMachinesChoice()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Video["outputWidth"] = 1280;
        obs.Video["outputHeight"] = 720;

        Assert.DoesNotContain(
            Validate(obs).Findings,
            finding => finding.Code == ObsValidator.CanvasMismatch
        );
    }

    [Fact]
    public void LowFps_IsAWarning()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Video["fpsNumerator"] = 24;
        obs.Video["fpsDenominator"] = 1;

        ObsFinding finding = Single(Validate(obs), ObsValidator.FpsLow);

        Assert.Equal(ObsValidator.Warning, finding.Severity);
        Assert.Equal("24", finding.Subject);
    }

    [Theory]
    [InlineData(true, ObsValidator.Error)]
    [InlineData(false, ObsValidator.Warning)]
    public void AnMkvRecording_IsAnErrorOnlyWhenThisInstallRecords(bool recording, string severity)
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.ProfileParameters[("SimpleOutput", "RecFormat2")] = "mkv";
        OBSSettings settings = FakeObs.Settings();
        settings.RecordingEnabled = recording;

        ObsFinding finding = Single(Validate(obs, settings), ObsValidator.RecordingFormat);

        Assert.Equal(severity, finding.Severity);
        Assert.Equal("mkv", finding.Subject);
        Assert.Contains(".mp4", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Standard", "AdvOut", "RecFormat2", "hybrid_mp4", false)]
    [InlineData("Standard", "AdvOut", "RecFormat2", "mov", true)]
    [InlineData("FFmpeg", "AdvOut", "FFExtension", "mp4", false)]
    [InlineData("FFmpeg", "AdvOut", "FFExtension", "mkv", true)]
    public void AdvancedOutput_ReadsItsOwnContainer(
        string recType,
        string category,
        string name,
        string format,
        bool finding
    )
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.ProfileParameters[("Output", "Mode")] = "Advanced";
        obs.ProfileParameters[("AdvOut", "RecType")] = recType;
        obs.ProfileParameters[(category, name)] = format;

        Assert.Equal(
            finding,
            Validate(obs).Findings.Any(found => found.Code == ObsValidator.RecordingFormat)
        );
    }

    [Fact]
    public void AnUnreadableProfile_IsAWarning()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Failures["GetProfileParameter"] = new ObsRequestException(
            "GetProfileParameter",
            604,
            "No profile is loaded."
        );

        Assert.Equal(
            ObsValidator.Warning,
            Single(Validate(obs), ObsValidator.ProfileUnreadable).Severity
        );
    }

    [Fact]
    public void Streaming_WithoutAKey_IsAnErrorThatStopsTheStream()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.StreamService["streamServiceSettings"]["key"] = "";
        OBSSettings settings = FakeObs.Settings();
        settings.StreamingEnabled = true;

        ObsValidation validation = Validate(obs, settings);
        ObsFinding finding = Single(validation, ObsValidator.StreamKeyMissing);

        Assert.Equal(ObsValidator.Error, finding.Severity);
        Assert.Same(finding, ObsValidator.BlocksStream(validation));
        Assert.DoesNotContain(FakeObs.StreamKey, Describe(validation), StringComparison.Ordinal);
    }

    [Fact]
    public void Streaming_ToAnotherService_IsAWarning()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.StreamService["streamServiceSettings"]["service"] = "YouTube - RTMPS";
        OBSSettings settings = FakeObs.Settings();
        settings.StreamingEnabled = true;

        ObsValidation validation = Validate(obs, settings);
        ObsFinding finding = Single(validation, ObsValidator.StreamServiceUnexpected);

        Assert.Equal(ObsValidator.Warning, finding.Severity);
        Assert.Equal("YouTube - RTMPS", finding.Subject);
        Assert.Null(ObsValidator.BlocksStream(validation));
        Assert.DoesNotContain(FakeObs.StreamKey, Describe(validation), StringComparison.Ordinal);
    }

    [Fact]
    public void NotStreaming_DoesNotCheckTheStreamService()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.StreamService["streamServiceSettings"]["key"] = "";

        ObsValidation validation = Validate(obs);

        Assert.DoesNotContain(
            validation.Findings,
            finding => finding.Code == ObsValidator.StreamKeyMissing
        );
        Assert.DoesNotContain(obs.Requests, request => request == "GetStreamServiceSettings");
    }

    [Fact]
    public void AMissingFilter_IsAWarning()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Source("countdown")["filters"] = new Newtonsoft.Json.Linq.JArray();

        ObsFinding finding = Single(Validate(obs), ObsValidator.FilterMissing);

        Assert.Equal(ObsValidator.Warning, finding.Severity);
        Assert.Equal("countdown/Crop/Pad", finding.Subject);
    }

    [Fact]
    public void AMissingStreamRequest_StopsTheStream_AndAnUnmutedMic_DoesNot()
    {
        FakeObs missing = FakeObs.Installed(data);
        missing.MissingRequests.Add("StartStream");
        FakeObs mic = FakeObs.Installed(data);
        mic.Mic = "Mic/Aux";

        Assert.Equal(
            ObsValidator.RequestUnavailable,
            ObsValidator.BlocksStream(Validate(missing))?.Code
        );
        Assert.False(Validate(mic).Ok);
        Assert.Null(ObsValidator.BlocksStream(Validate(mic)));
    }

    [Theory]
    [InlineData("file:///C:/heroes%20replay/a.html?x=1#top", @"C:\heroes replay\a.html")]
    [InlineData("C:/heroesreplay/Data/OBS.txt", @"C:\heroesreplay\Data\OBS.txt")]
    [InlineData("Ranks/bronze.png", @"Ranks\bronze.png")]
    public void LocalPath_DropsTheUrlParts(string value, string expected)
    {
        Assert.Equal(expected, ObsValidator.LocalPath(value));
    }

    private ObsValidation Validate(FakeObs obs, OBSSettings settings = null) =>
        ObsValidator.Validate(obs.Open(null, null), FakeObs.InspectionSettings(data, settings));

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
