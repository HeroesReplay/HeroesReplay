using System;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsInspectorTests : IDisposable
{
    private readonly string data = Path.Combine(
        Path.GetTempPath(),
        "hr-obs-inspect-" + Path.GetRandomFileName()
    );

    public ObsInspectorTests()
    {
        Directory.CreateDirectory(data);
    }

    public void Dispose()
    {
        Directory.Delete(data, recursive: true);
    }

    [Fact]
    public void Inspect_ReadsTheLiveState()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Mic = "Mic/Aux";
        obs.MicMuted = true;

        ObsInspection inspection = Inspect(obs);

        Assert.True(inspection.Ok);
        Assert.Equal(1, inspection.SchemaVersion);
        Assert.Null(inspection.Code);
        Assert.Empty(inspection.Unread);
        Assert.Equal("ws://127.0.0.1:4455", inspection.Endpoint);
        Assert.Equal("32.2.2", inspection.Version.ObsVersion);
        Assert.Equal("5.6.3", inspection.Version.WebsocketVersion);
        Assert.True(inspection.Version.AvailableRequestCount >= ObsReadOnly.Requests.Count);

        Assert.True(inspection.Selection.Ok);
        Assert.True(inspection.Selection.ProfileMatches);
        Assert.True(inspection.Selection.CollectionMatches);
        Assert.Contains("Untitled", inspection.Selection.Profiles);

        Assert.Equal(1920, inspection.Video.BaseWidth);
        Assert.Equal(720, inspection.Video.OutputHeight);
        Assert.Equal(59.94, inspection.Video.Fps);

        Assert.Equal("game-scene", inspection.ProgramScene);
        ObsSceneInfo game = inspection.Scenes.Single(scene => scene.Name == "game-scene");
        ObsSceneItemInfo rank = game.Items.Single(item => item.Name == "gold-image");
        Assert.Equal("image_source", rank.Kind);
        Assert.False(rank.Enabled);
        Assert.True(game.Items.Single(item => item.Name == "game-capture").Enabled);

        ObsInputInfo desktop = inspection.Inputs.Single(input => input.Name == "Desktop Audio");
        Assert.Equal("desktop1", desktop.GlobalAudio);
        Assert.False(desktop.Muted);
        Assert.Equal(1.0, desktop.VolumeMul);
        ObsInputInfo mic = inspection.Inputs.Single(input => input.Name == "Mic/Aux");
        Assert.Equal("mic1", mic.GlobalAudio);
        Assert.True(mic.Muted);
        ObsInputInfo browser = inspection.Inputs.Single(input =>
            input.Name == "match-report-browser"
        );
        Assert.Equal("browser_source", browser.UnversionedKind);
        Assert.Null(browser.GlobalAudio);
        Assert.Null(browser.Muted);
        Assert.Null(browser.VolumeMul);

        Assert.False(inspection.Stream.Active);
        Assert.Equal(3, inspection.Stream.DroppedFrames);
        Assert.True(inspection.Record.Active);
        Assert.Equal(60000, inspection.Record.DurationMs);
        Assert.Equal(12.35, inspection.Stats.CpuUsagePercent);
        Assert.Equal(1, inspection.Stats.RenderSkippedFrames);
        Assert.Equal(2, inspection.Stats.OutputSkippedFrames);
        Assert.Equal(new ObsStreamService("rtmp_common", true), inspection.StreamService);
        Assert.False(inspection.StreamArm.Armed);
        Assert.False(inspection.StreamArm.StreamingEnabled);
        Assert.False(inspection.StreamArm.MayStart);
        Assert.Null(inspection.StreamArm.BlockedBy);
    }

    [Fact]
    public void Inspect_WrongProfile_IsReportedNotFailed()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Profile = "Untitled";

        ObsInspection inspection = Inspect(obs);

        Assert.True(inspection.Ok);
        Assert.False(inspection.Selection.Ok);
        Assert.False(inspection.Selection.ProfileMatches);
        Assert.Equal("Untitled", inspection.Selection.ActiveProfile);
        Assert.Equal(ObsSelection.ProfileMismatch, inspection.Selection.Reason);
    }

    [Fact]
    public void Inspect_AFailedRequest_LeavesThatSectionEmpty()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Failures["GetStats"] = new ObsRequestException("GetStats", 702, "Busy.");

        ObsInspection inspection = Inspect(obs);

        Assert.True(inspection.Ok);
        Assert.Null(inspection.Stats);
        Assert.NotNull(inspection.Video);
        Assert.Contains(
            inspection.Unread,
            line => line.StartsWith("GetStats failed", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Inspect_StreamingWithoutTheArm_IsBlocked()
    {
        OBSSettings settings = FakeObs.Settings();
        settings.StreamingEnabled = true;

        ObsInspection unarmed = ObsInspector.Inspect(
            FakeObs.Installed(data).Open(null, null),
            FakeObs.InspectionSettings(data, settings)
        );
        ObsInspection armed = ObsInspector.Inspect(
            FakeObs.Installed(data).Open(null, null),
            FakeObs.InspectionSettings(data, settings, armed: true)
        );

        Assert.False(unarmed.StreamArm.MayStart);
        Assert.Equal(ObsStreamArm.NotArmedReason, unarmed.StreamArm.BlockedBy);
        Assert.True(armed.StreamArm.MayStart);
        Assert.Null(armed.StreamArm.BlockedBy);
    }

    [Fact]
    public void StreamService_KeepsOnlyTheTypeAndWhetherAKeyIsSet()
    {
        var response = new JObject
        {
            ["streamServiceType"] = "rtmp_common",
            ["streamServiceSettings"] = new JObject
            {
                ["service"] = "Twitch",
                ["server"] = "auto",
                ["key"] = FakeObs.StreamKey,
            },
        };

        ObsStreamService service = ObsStreamService.Summarize(response);

        Assert.Equal("rtmp_common", service.Type);
        Assert.True(service.KeySet);
        Assert.Equal(
            new[] { nameof(ObsStreamService.KeySet), nameof(ObsStreamService.Type) },
            typeof(ObsStreamService).GetProperties().Select(property => property.Name).Order()
        );
        Assert.False(ObsStreamService.Summarize(new JObject()).KeySet);
        Assert.False(ObsStreamService.Summarize(null).KeySet);
    }

    private ObsInspection Inspect(FakeObs obs) =>
        ObsInspector.Inspect(obs.Open(null, null), FakeObs.InspectionSettings(data));
}
