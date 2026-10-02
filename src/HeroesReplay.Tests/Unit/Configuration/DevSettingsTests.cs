using System;
using System.IO;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Spectating.Capture;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HeroesReplay.Tests.Unit.Configuration;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class DevSettingsTests
{
    [Fact]
    public void DevOverlay_UsesAsaServerPathsAndPrintWindow()
    {
        string basePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        string devPath = Path.Combine(AppContext.BaseDirectory, "appsettings.dev.json");
        Assert.True(File.Exists(devPath), $"expected {devPath} to be copied to the test output.");

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(basePath)
            .AddJsonFile(devPath)
            .Build();
        LocationSettings location = configuration.GetSection("Location").Get<LocationSettings>();
        CaptureSettings capture = configuration.GetSection("Capture").Get<CaptureSettings>();

        Assert.NotNull(location);
        Assert.Equal(@"C:\Program Files (x86)\Heroes of the Storm", location.GameInstallDirectory);
        Assert.Equal(@"C:\heroesreplay\Replays", location.ReplaySource);
        Assert.Equal(@"C:\heroesreplay\Data", location.DataDirectory);
        Assert.Equal(@"C:\heroesreplay\Data\Clients", location.RetainedClientDirectory);
        Assert.Equal(@"C:\heroesreplay\Battle.net\Battle.net.exe", location.BattlenetPath);
        Assert.NotNull(capture);
        Assert.Equal(CaptureMethod.PrintWindow, capture.Method);
        Assert.False(configuration.GetValue<bool>("OBS:StreamingEnabled"));
        Assert.True(configuration.GetValue<bool>("YouTube:DryRun"));
        // The dev box shares the live Twitch channel. A dev prediction would cancel the live one.
        Assert.False(configuration.GetValue<bool>("Twitch:EnablePredictions"));
    }

    [Fact]
    public void Settings_OmitUnusedS3AndLegacyEndpoints()
    {
        string basePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        string devPath = Path.Combine(AppContext.BaseDirectory, "appsettings.dev.json");
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(basePath)
            .AddJsonFile(devPath)
            .Build();

        Assert.Null(configuration["HeroesProfileApi:S3Bucket"]);
        Assert.Null(configuration["HeroesProfileApi:S3Region"]);
        Assert.Null(configuration["HeroesProfileApi:BaseUri"]);
        Assert.Null(configuration["HeroesProfileApi:OpenApiBaseUri"]);
        Assert.Null(configuration["HeroesProfileApi:EnableMMR"]);
        Assert.Null(configuration["Twitch:RefreshUri"]);
        Assert.Null(configuration["Twitch:EnableTwitchClips"]);
        Assert.Null(configuration["Quotes:Subscriber"]);
        Assert.False(
            string.IsNullOrWhiteSpace(configuration["HeroesProfileApi:ExternalV1BaseUri"])
        );
    }
}
