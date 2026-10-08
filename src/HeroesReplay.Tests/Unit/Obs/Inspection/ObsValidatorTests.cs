using System;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Obs.Inspection;
using Newtonsoft.Json.Linq;
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
        File.WriteAllText(Path.Combine(data, ReleaseVersionLabel.FileName), "v1.0.0-614");

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
    public void AnObsWebSocketBefore53_StopsTheStreamAndNamesItsVersion()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.WebSocketVersion = "5.2.3";
        obs.MissingRequests.Add("SetRecordDirectory");

        ObsValidation validation = Validate(obs);
        ObsFinding finding = Single(validation, ObsValidator.RequestUnavailable);

        Assert.False(validation.Ok);
        Assert.Equal(ObsValidator.Error, finding.Severity);
        Assert.Equal("SetRecordDirectory", finding.Subject);
        Assert.Contains(
            "obs-websocket 5.2.3 does not offer SetRecordDirectory",
            finding.Message,
            StringComparison.Ordinal
        );
        Assert.Contains("Update OBS Studio", finding.Message, StringComparison.Ordinal);
        Assert.Same(finding, ObsValidator.BlocksStream(validation));
    }

    [Fact]
    public void NoRequestList_FailsClosedOnEveryRequiredRequest()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.OmitAvailableRequests = true;

        ObsValidation validation = Validate(obs);
        var unavailable = validation
            .Findings.Where(finding => finding.Code == ObsValidator.RequestUnavailable)
            .ToList();

        Assert.False(validation.Ok);
        Assert.Equal(ObsValidator.RequestUnavailable, validation.Code);
        Assert.Equal(
            ObsValidator.RequiredRequests.OrderBy(name => name, StringComparer.Ordinal),
            unavailable.Select(finding => finding.Subject)
        );
        Assert.All(unavailable, finding => Assert.Equal(ObsValidator.Error, finding.Severity));
        Assert.Equal(ObsValidator.RequestUnavailable, ObsValidator.BlocksStream(validation)?.Code);
    }

    [Fact]
    public void ExtraRequestsAndANewerObsWebSocket_AreNotFindings()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.WebSocketVersion = "6.0.0";

        Assert.DoesNotContain(
            Validate(obs).Findings,
            finding => finding.Code == ObsValidator.RequestUnavailable
        );
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
    public void AnEnabledScrollFilter_TheCollectionDoesNotHave_IsStale()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Source("match-report-browser")["filters"] = new Newtonsoft.Json.Linq.JArray(
            new Newtonsoft.Json.Linq.JObject
            {
                ["name"] = "Scroll",
                ["id"] = "scroll_filter",
                ["enabled"] = true,
            }
        );

        ObsFinding finding = Single(Validate(obs), ObsValidator.FilterStale);

        Assert.Equal(ObsValidator.Warning, finding.Severity);
        Assert.Equal("match-report-browser/Scroll", finding.Subject);
    }

    [Fact]
    public void ADisabledScrollFilter_IsNotAFinding()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Source("match-report-browser")["filters"] = new Newtonsoft.Json.Linq.JArray(
            new Newtonsoft.Json.Linq.JObject
            {
                ["name"] = "Scroll",
                ["id"] = "scroll_filter",
                ["enabled"] = false,
            }
        );

        Assert.DoesNotContain(
            Validate(obs).Findings,
            finding => finding.Code == ObsValidator.FilterStale
        );
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

    [Fact]
    public void TheTemplate_AnchorsCurrentReplayBottomLeft()
    {
        // #276 moved the replay info to the bottom-left corner; obs/Default.json is the truth.
        ObsPlacement placement = ObsCollectionPaths.ScenePlacements(
            File.ReadAllText(Path.Combine(FakeObs.RepoObsDirectory(), "Default.json"))
        )["game-scene"]["current-replay"];

        Assert.Equal(5, placement.X);
        Assert.Equal(1060, placement.Y);
        Assert.Equal(9, placement.Alignment);
        Assert.Equal("bottom-left (9)", ObsPlacement.AlignmentName(placement.Alignment));
        Assert.Equal(ObsPlacement.NoBounds, placement.BoundsType);
    }

    [Fact]
    public void FromTransform_ReadsWhatObs32Answers()
    {
        // GetSceneItemTransform for game-scene/current-replay on ASA-SERVER (OBS 32.2.2).
        JObject transform = JObject.Parse(
            """
            {
              "alignment": 9, "boundsAlignment": 0, "boundsHeight": 0,
              "boundsType": "OBS_BOUNDS_NONE", "boundsWidth": 0,
              "cropBottom": 0, "cropLeft": 0, "cropRight": 0, "cropToBounds": false, "cropTop": 0,
              "height": 203.95938110351562, "positionX": 5, "positionY": 1060, "rotation": 0,
              "scaleX": 0.4154411852359772, "scaleY": 0.41624364256858826,
              "sourceHeight": 490, "sourceWidth": 384, "width": 159.5294189453125
            }
            """
        );
        ObsPlacement template = ObsCollectionPaths.ScenePlacements(
            File.ReadAllText(Path.Combine(FakeObs.RepoObsDirectory(), "Default.json"))
        )["game-scene"]["current-replay"];

        ObsPlacement live = ObsPlacement.FromTransform(transform);

        Assert.Equal(
            new ObsPlacement(
                5,
                1060,
                9,
                0.4154411852359772,
                0.41624364256858826,
                ObsPlacement.NoBounds,
                0,
                0
            ),
            live
        );
        Assert.Empty(live.Differences(template));
    }

    [Fact]
    public void AMovedItem_IsMisplaced_AndOnlyAWarning()
    {
        FakeObs obs = FakeObs.Installed(data);
        JObject item = obs.SceneItem("game-scene", "current-replay");
        item["pos"]["y"] = 1040;
        item["align"] = 5;

        ObsValidation validation = Validate(obs);

        Assert.True(validation.Ok, Describe(validation));
        ObsFinding finding = Single(validation, ObsValidator.SceneItemMisplaced);
        Assert.Equal(ObsValidator.Warning, finding.Severity);
        Assert.Equal("game-scene/current-replay", finding.Subject);
        Assert.Contains("at (5, 1040) instead of (5, 1060)", finding.Message);
        Assert.Contains("anchored top-left (5) instead of bottom-left (9)", finding.Message);
        Assert.Contains("GetSceneItemTransform", string.Join(",", obs.Requests));
        Assert.Null(ObsValidator.BlocksStream(validation));
    }

    [Fact]
    public void AnItemWithinTolerance_IsInPlace()
    {
        FakeObs obs = FakeObs.Installed(data);
        JObject item = obs.SceneItem("game-scene", "current-replay");
        item["pos"]["x"] = 5 + ObsPlacement.PixelTolerance - 0.5;
        item["pos"]["y"] = 1060 - ObsPlacement.PixelTolerance;
        item["scale"]["x"] = (double)item["scale"]["x"] + ObsPlacement.ScaleTolerance / 2;

        Assert.DoesNotContain(
            Validate(obs).Findings,
            finding => finding.Code == ObsValidator.SceneItemMisplaced
        );
    }

    [Fact]
    public void AMovedReportPageAndAResizedGameCapture_AreMisplaced()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.SceneItem("match-report", "match-report-browser")["pos"]["x"] = 240;
        JObject capture = obs.SceneItem("game-scene", "game-capture");
        capture["bounds"]["x"] = 1280;
        capture["bounds"]["y"] = 720;
        // The scale does not count for an item with a bounding box.
        capture["scale"]["x"] = 0.5;

        ObsValidation validation = Validate(obs);

        Assert.Equal(
            ["game-scene/game-capture", "match-report/match-report-browser"],
            validation
                .Findings.Where(finding => finding.Code == ObsValidator.SceneItemMisplaced)
                .Select(finding => finding.Subject)
        );
        Assert.Contains(
            "bounded to 1280x720 instead of 1920x1080",
            Misplaced(validation, "game-scene/game-capture").Message
        );
        Assert.DoesNotContain("scaled", Misplaced(validation, "game-scene/game-capture").Message);
    }

    [Fact]
    public void AnItemWithAnotherBoundingBoxOrScale_IsMisplaced()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.SceneItem("game-scene", "game-capture")["bounds_type"] = 1;
        obs.SceneItem("game-scene", "rank-points")["scale"]["y"] = 2.0;

        ObsValidation validation = Validate(obs);

        Assert.Contains(
            "bounding box stretch instead of scale inner",
            Misplaced(validation, "game-scene/game-capture").Message
        );
        Assert.Contains(
            "scaled 1.14x2 instead of 1.14x1.143",
            Misplaced(validation, "game-scene/rank-points").Message
        );
    }

    [Fact]
    public void AnUnreadableTransform_IsSkipped()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.SceneItem("game-scene", "current-replay")["pos"]["y"] = 10;
        obs.Failures["GetSceneItemTransform"] = new ObsRequestException(
            "GetSceneItemTransform",
            204,
            "Unknown request type."
        );

        ObsValidation validation = Validate(obs);

        Assert.DoesNotContain(
            validation.Findings,
            finding => finding.Code == ObsValidator.SceneItemMisplaced
        );
        Assert.True(validation.Ok, Describe(validation));
    }

    [Fact]
    public void AStreamBitrateBelowTheFloor_IsAWarning()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Video["outputWidth"] = 1920;
        obs.Video["outputHeight"] = 1080;
        obs.ProfileParameters[("SimpleOutput", "VBitrate")] = "2500";

        ObsValidation validation = Validate(obs);

        Assert.True(validation.Ok, Describe(validation));
        ObsFinding finding = Single(validation, ObsValidator.BitrateLow);
        Assert.Equal(ObsValidator.Warning, finding.Severity);
        Assert.Equal("stream", finding.Subject);
        Assert.Contains("2500 kbps and records at the same bitrate", finding.Message);
        Assert.Contains("4500 kbps floor for 1920x1080 at 59.94 FPS", finding.Message);
        Assert.Null(ObsValidator.BlocksStream(validation));
    }

    [Fact]
    public void AStreamBitrateAboveTheFloor_IsNotAFinding()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Video["outputWidth"] = 1920;
        obs.Video["outputHeight"] = 1080;
        obs.ProfileParameters[("SimpleOutput", "VBitrate")] = "6000";

        Assert.DoesNotContain(
            Validate(obs).Findings,
            finding => finding.Code == ObsValidator.BitrateLow
        );
    }

    [Fact]
    public void AProfileWithoutABitrate_IsNotAFinding()
    {
        FakeObs simple = FakeObs.Installed(data);
        simple.ProfileParameters.Remove(("SimpleOutput", "VBitrate"));
        FakeObs advanced = FakeObs.Installed(data);
        advanced.ProfileParameters[("Output", "Mode")] = "Advanced";
        advanced.ProfileParameters[("AdvOut", "RecFormat2")] = "hybrid_mp4";
        advanced.ProfileParameters[("AdvOut", "Encoder")] = "obs_qsv11_v2";

        foreach (FakeObs obs in new[] { simple, advanced })
        {
            ObsValidation validation = Validate(obs);
            Assert.DoesNotContain(
                validation.Findings,
                finding => finding.Code == ObsValidator.BitrateLow
            );
            Assert.True(validation.Ok, Describe(validation));
        }
    }

    [Fact]
    public void ACustomFfmpegRecordingBelowTheFloor_IsARecordingWarning()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.ProfileParameters[("Output", "Mode")] = "Advanced";
        obs.ProfileParameters[("AdvOut", "RecType")] = "FFmpeg";
        obs.ProfileParameters[("AdvOut", "FFExtension")] = "mp4";
        obs.ProfileParameters[("AdvOut", "FFVBitrate")] = "1500";

        ObsFinding finding = Single(Validate(obs), ObsValidator.BitrateLow);

        Assert.Equal("recording", finding.Subject);
        Assert.Contains("1500 kbps, below the 3000 kbps floor", finding.Message);
    }

    [Theory]
    [InlineData(1080L, 60.0, 4500L)]
    [InlineData(1080L, 59.94, 4500L)]
    [InlineData(1080L, 30.0, 3000L)]
    [InlineData(720L, 60.0, 3000L)]
    [InlineData(720L, 30.0, 2000L)]
    [InlineData(480L, 60.0, 2000L)]
    [InlineData(null, 60.0, null)]
    [InlineData(1080L, null, null)]
    public void BitrateFloor_FollowsTheOutputSizeAndFps(long? height, double? fps, long? floor)
    {
        Assert.Equal(floor, ObsBitratePolicy.FloorKbps(height, fps));
    }

    [Theory]
    [InlineData(0, "center (0)")]
    [InlineData(5, "top-left (5)")]
    [InlineData(9, "bottom-left (9)")]
    [InlineData(10, "bottom-right (10)")]
    [InlineData(4, "top (4)")]
    [InlineData(2, "right (2)")]
    public void Alignment_IsNamedWithItsValue(int alignment, string name)
    {
        Assert.Equal(name, ObsPlacement.AlignmentName(alignment));
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

    private static ObsFinding Misplaced(ObsValidation validation, string subject) =>
        Assert.Single(
            validation.Findings,
            finding => finding.Code == ObsValidator.SceneItemMisplaced && finding.Subject == subject
        );

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
