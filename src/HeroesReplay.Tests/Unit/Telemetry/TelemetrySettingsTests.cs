using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Xunit;

namespace HeroesReplay.Tests.Unit.Telemetry;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class TelemetrySettingsTests
{
    [Fact]
    public void AppSettings_BindsTheOpenTelemetrySection()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        Assert.True(File.Exists(path), $"expected {path} to be copied to the test output.");
        IConfigurationRoot configuration = new ConfigurationBuilder().AddJsonFile(path).Build();

        TelemetrySettings settings = configuration
            .GetSection(TelemetrySettings.SectionName)
            .Get<TelemetrySettings>();

        Assert.NotNull(settings);
        Assert.True(settings.Enabled);
        Assert.Equal(1.0, settings.Traces.SampleRatio);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.Metrics.ExportInterval);
        Assert.Equal(LogLevel.Information, settings.Logs.MinimumLevel);
        Assert.Equal(512, settings.Export.MaxQueueSize);
        Assert.True(settings.Dashboard.Supervise);
        Assert.Equal(1000, settings.Dashboard.MaxTraceCount);
        Assert.Equal(
            new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15) },
            settings.Dashboard.EffectiveRestartDelays
        );
    }

    [Fact]
    public void DashboardArguments_PassEveryLimitAndSkipZero()
    {
        var settings = new TelemetryDashboardSettings
        {
            MaxTraceCount = 500,
            MaxLogCount = 0,
            MaxSpanEventCount = -1,
        };

        IReadOnlyList<string> arguments = settings.DashboardArguments();

        Assert.Contains("--Dashboard:TelemetryLimits:MaxTraceCount=500", arguments);
        Assert.Contains("--Dashboard:TelemetryLimits:MaxMetricsCount=10000", arguments);
        Assert.Contains("--Dashboard:TelemetryLimits:MaxAttributeLength=2048", arguments);
        Assert.DoesNotContain(arguments, argument => argument.Contains("MaxLogCount"));
        Assert.DoesNotContain(arguments, argument => argument.Contains("MaxSpanEventCount"));
    }

    [Fact]
    public void ProcessEnvironment_DefaultsToASmallWorkstationGc()
    {
        IReadOnlyDictionary<string, string> environment =
            new TelemetryDashboardSettings().ProcessEnvironment();

        Assert.Equal("0", environment["DOTNET_gcServer"]);
        Assert.Equal("5", environment["DOTNET_GCConserveMemory"]);
        Assert.False(environment.ContainsKey("DOTNET_GCHeapHardLimit"));
    }

    [Fact]
    public void ProcessEnvironment_HeapLimitIsHexBytes()
    {
        IReadOnlyDictionary<string, string> environment = new TelemetryDashboardSettings
        {
            GcHeapHardLimitMb = 256,
            WorkstationGc = false,
            GcConserveMemory = 0,
        }.ProcessEnvironment();

        Assert.Equal("0x10000000", environment["DOTNET_GCHeapHardLimit"]);
        Assert.Single(environment);
    }

    [Theory]
    [InlineData(1.0, typeof(AlwaysOnSampler))]
    [InlineData(5.0, typeof(AlwaysOnSampler))]
    [InlineData(0.0, typeof(AlwaysOffSampler))]
    [InlineData(0.25, typeof(TraceIdRatioBasedSampler))]
    public void CreateSampler_IsParentBasedOverTheRatio(double ratio, Type root)
    {
        Sampler sampler = HeroesReplayOpenTelemetry.CreateSampler(
            new TelemetryTraceSettings { SampleRatio = ratio }
        );

        Assert.IsType<ParentBasedSampler>(sampler);
        Assert.Contains(root.Name, sampler.Description);
    }

    [Fact]
    public void Export_ClampsBatchToTheQueue()
    {
        var export = new TelemetryExportSettings { MaxQueueSize = 64, MaxExportBatchSize = 1000 };

        Assert.Equal(64, export.EffectiveMaxQueueSize);
        Assert.Equal(64, export.EffectiveMaxExportBatchSize);
    }
}
