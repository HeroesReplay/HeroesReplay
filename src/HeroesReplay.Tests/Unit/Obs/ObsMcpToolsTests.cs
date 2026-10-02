using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HeroesReplay.CLI.Mcp;
using HeroesReplay.Core.Obs;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsMcpToolsTests : IDisposable
{
    private readonly string data = Path.Combine(
        Path.GetTempPath(),
        "hr-obs-mcp-" + Path.GetRandomFileName()
    );

    public ObsMcpToolsTests()
    {
        Directory.CreateDirectory(data);
    }

    public void Dispose()
    {
        Directory.Delete(data, recursive: true);
    }

    [Fact]
    public void EveryTool_SendsOnlyAllowedGetRequests()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Mic = "Mic/Aux";
        ObsMcpTools tools = Tools(obs);

        tools.Inspect();
        tools.Validate();
        tools.Screenshot();
        tools.Screenshot("match-report", 1280);

        Assert.NotEmpty(obs.Requests);
        string[] notGet = obs
            .Requests.Where(request => !request.StartsWith("Get", StringComparison.Ordinal))
            .Distinct()
            .ToArray();
        Assert.True(notGet.Length == 0, "Non-Get OBS requests: " + string.Join(", ", notGet));
        Assert.All(obs.Requests, request => Assert.Contains(request, ObsReadOnly.Requests));

        // The enumeration covers the whole surface, including the sensitive and the heavy reads.
        Assert.Contains("GetStreamServiceSettings", obs.Requests);
        Assert.Contains("GetSourceScreenshot", obs.Requests);
        Assert.Contains("GetInputSettings", obs.Requests);
        Assert.Contains("GetSceneItemList", obs.Requests);
        Assert.Equal(4, obs.Opened);
        Assert.Equal(obs.Opened, obs.Disposed);
    }

    [Fact]
    public void Tools_AreNamedAndAnnotatedReadOnly()
    {
        McpServerToolAttribute[] tools = typeof(ObsMcpTools)
            .GetMethods()
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attribute => attribute != null)
            .ToArray();

        Assert.Equal(
            new[] { "obs_inspect", "obs_screenshot", "obs_validate" },
            tools.Select(tool => tool.Name).Order()
        );
        Assert.All(
            tools,
            tool =>
            {
                Assert.True(tool.ReadOnly);
                Assert.False(tool.Destructive);
            }
        );
    }

    [Fact]
    public void Inspect_NeverReturnsTheStreamKeyOrServerCredentials()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.StreamService = new JObject
        {
            ["streamServiceType"] = "rtmp_custom",
            ["streamServiceSettings"] = new JObject
            {
                ["server"] = "rtmp://SERVER-SECRET@ingest.example/app",
                ["key"] = FakeObs.StreamKey,
                ["use_auth"] = true,
                ["username"] = "USERNAME-SECRET",
                ["password"] = "PASSWORD-SECRET",
                ["bearer_token"] = "BEARER-SECRET",
            },
        };

        ObsInspection inspection = Tools(obs).Inspect();
        string[] outputs =
        {
            JsonSerializer.Serialize(inspection, McpJsonUtilities.DefaultOptions),
            JsonSerializer.Serialize(inspection),
            inspection.ToString(),
        };

        Assert.Contains("GetStreamServiceSettings", obs.Requests);
        Assert.Equal(new ObsStreamService("rtmp_custom", true), inspection.StreamService);
        foreach (string output in outputs)
        {
            Assert.DoesNotContain(FakeObs.StreamKey, output, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET", output, StringComparison.Ordinal);
            Assert.DoesNotContain("ingest.example", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Inspect_ReportsAnEmptyKeyAsNotSet()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.StreamService["streamServiceSettings"]["key"] = " ";

        Assert.False(Tools(obs).Inspect().StreamService.KeySet);
    }

    [Fact]
    public void Inspect_UsesTheConfiguredEndpointAndPassword()
    {
        FakeObs obs = FakeObs.Installed(data);
        OBSSettings settings = FakeObs.Settings();
        settings.WebSocketEndpoint = "ws://127.0.0.1:4460";
        settings.WebSocketPassword = "unit-password";

        ObsInspection inspection = Tools(obs, settings).Inspect();

        Assert.True(inspection.Ok);
        Assert.Equal("ws://127.0.0.1:4460", obs.OpenedEndpoint);
        Assert.Equal("unit-password", obs.OpenedPassword);
        Assert.DoesNotContain(
            "unit-password",
            JsonSerializer.Serialize(inspection, McpJsonUtilities.DefaultOptions),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ClosedObs_IsAStableCodeOnEveryTool()
    {
        var closed = new ClosedObs();
        ObsMcpTools tools = new(closed, () => FakeObs.InspectionSettings(data));

        ObsInspection inspection = tools.Inspect();
        ObsValidation validation = tools.Validate();
        CallToolResult screenshot = tools.Screenshot();

        Assert.False(inspection.Ok);
        Assert.Equal(ObsUnavailableException.Unreachable, inspection.Code);
        Assert.Contains("Start OBS", inspection.Error, StringComparison.Ordinal);
        Assert.NotNull(inspection.StreamArm);
        Assert.False(inspection.StreamArm.MayStart);
        Assert.False(validation.Ok);
        Assert.Equal(ObsUnavailableException.Unreachable, validation.Code);
        Assert.True(screenshot.IsError);
        Assert.StartsWith(
            "obs.unreachable: ",
            Assert.IsType<TextContentBlock>(Assert.Single(screenshot.Content)).Text,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void UnreadableSettings_AreReportedNotThrown()
    {
        ObsMcpTools tools = new(
            FakeObs.Installed(data),
            () => throw new InvalidDataException("bad appsettings")
        );

        ObsInspection inspection = tools.Inspect();

        Assert.False(inspection.Ok);
        Assert.Equal(ObsMcpTools.SettingsUnreadable, inspection.Code);
        Assert.Contains("bad appsettings", inspection.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Screenshot_ReturnsTheWholePngAsAnImageBlock()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Png = TinyPng.Create(96, 54);

        CallToolResult result = Tools(obs).Screenshot();

        Assert.NotEqual(true, result.IsError);
        ImageContentBlock image = Assert.IsType<ImageContentBlock>(result.Content[0]);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(obs.Png, image.DecodedData.ToArray());
        Assert.True(Convert.ToBase64String(obs.Png).Length > 1000);
        TextContentBlock text = Assert.IsType<TextContentBlock>(result.Content[1]);
        Assert.Contains("Program scene 'game-scene'", text.Text, StringComparison.Ordinal);
        Assert.Contains("96x54", text.Text, StringComparison.Ordinal);
        JObject request = obs.Sent.Single(sent => sent.Type == "GetSourceScreenshot").Data;
        Assert.Equal("game-scene", (string)request["sourceName"]);
        Assert.Equal("png", (string)request["imageFormat"]);
        Assert.Equal(960, (int)request["imageWidth"]);
    }

    [Theory]
    [InlineData(5000, 1920)]
    [InlineData(1920, 1920)]
    [InlineData(640, 640)]
    [InlineData(3, 8)]
    [InlineData(0, 960)]
    [InlineData(-1, 960)]
    public void Screenshot_CapsTheWidth(int requested, int sent)
    {
        FakeObs obs = FakeObs.Installed(data);

        Tools(obs).Screenshot("waiting-screen", requested);

        JObject request = obs.Sent.Single(item => item.Type == "GetSourceScreenshot").Data;
        Assert.Equal("waiting-screen", (string)request["sourceName"]);
        Assert.Equal(sent, (int)request["imageWidth"]);
    }

    [Fact]
    public void Screenshot_OfAnUnknownSource_IsAnErrorWithACode()
    {
        CallToolResult result = Tools(FakeObs.Installed(data)).Screenshot("no-such-source");

        Assert.True(result.IsError);
        Assert.StartsWith(
            ObsMcpTools.SourceNotFound + ": ",
            Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text,
            StringComparison.Ordinal
        );
    }

    private ObsMcpTools Tools(FakeObs obs, OBSSettings settings = null) =>
        new(obs, () => FakeObs.InspectionSettings(data, settings));

    private sealed class ClosedObs : IObsReadSessionFactory
    {
        public IObsReadSession Open(string endpoint, string password) =>
            throw new ObsUnavailableException(
                ObsUnavailableException.Unreachable,
                "OBS did not answer at " + endpoint + ". Start OBS Studio."
            );
    }
}
