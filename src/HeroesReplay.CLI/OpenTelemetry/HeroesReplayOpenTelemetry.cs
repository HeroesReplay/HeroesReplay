using System;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.ServiceHost.Logs;
using HeroesReplay.Core.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HeroesReplay.CLI.OpenTelemetry;

public static class HeroesReplayOpenTelemetry
{
    public const string DefaultOtlpEndpoint = AspireDashboardHost.OtlpGrpcEndpoint;
    public const string DefaultServiceName = "heroesreplay";

    public static IServiceCollection AddHeroesReplayOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        TelemetrySettings settings = configuration
            .GetSection(TelemetrySettings.SectionName)
            .Get<TelemetrySettings>()
            ?? new TelemetrySettings();
        if (!settings.Enabled)
        {
            return services;
        }

        string endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? settings.OtlpEndpoint
            ?? DefaultOtlpEndpoint;

        serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
            ?? serviceName
            ?? settings.ServiceName
            ?? DefaultServiceName;

        Uri otlpUri = new(endpoint);
        TelemetryExportSettings export = settings.Export ?? new TelemetryExportSettings();
        TelemetryTraceSettings traces = settings.Traces ?? new TelemetryTraceSettings();
        TelemetryMetricSettings metrics = settings.Metrics ?? new TelemetryMetricSettings();
        TelemetryLogSettings logs = settings.Logs ?? new TelemetryLogSettings();
        ResourceBuilder resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(serviceName: serviceName, serviceNamespace: "HeroesReplay");

        OpenTelemetryBuilder builder = services
            .AddOpenTelemetry()
            .ConfigureResource(resource =>
                resource.AddService(serviceName: serviceName, serviceNamespace: "HeroesReplay")
            );

        if (traces.Enabled)
        {
            builder.WithTracing(tracing =>
            {
                tracing.AddSource(HeroesReplayTelemetry.SourceName);
                tracing.SetSampler(CreateSampler(traces));
                if (traces.HttpClient)
                {
                    tracing.AddHttpClientInstrumentation();
                }

                tracing.AddOtlpExporter(exporter =>
                {
                    Configure(exporter, otlpUri, export);
                    ApplyBatch(exporter.BatchExportProcessorOptions, export);
                });
            });
        }

        if (metrics.Enabled)
        {
            builder.WithMetrics(meter =>
            {
                meter.AddMeter(HeroesReplayTelemetry.SourceName);
                if (metrics.Runtime)
                {
                    meter.AddRuntimeInstrumentation();
                }

                if (metrics.HttpClient)
                {
                    meter.AddHttpClientInstrumentation();
                }

                meter.AddOtlpExporter(
                    (exporter, reader) =>
                    {
                        Configure(exporter, otlpUri, export);
                        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                            metrics.EffectiveExportIntervalMilliseconds;
                        reader.PeriodicExportingMetricReaderOptions.ExportTimeoutMilliseconds =
                            export.TimeoutMilliseconds;
                    }
                );
            });
        }

        if (logs.Enabled)
        {
            services.AddLogging(logging =>
            {
                // Only what goes to the dashboard: the role log files keep their own level.
                logging.AddFilter<OpenTelemetryLoggerProvider>(null, logs.MinimumLevel);
                logging.AddOpenTelemetry(otel =>
                {
                    otel.IncludeFormattedMessage = true;
                    otel.IncludeScopes = logs.IncludeScopes;
                    otel.ParseStateValues = true;
                    otel.SetResourceBuilder(resourceBuilder);
                    otel.AddOtlpExporter(
                        (exporter, processor) =>
                        {
                            Configure(exporter, otlpUri, export);
                            ApplyBatch(processor.BatchExportProcessorOptions, export);
                        }
                    );
                });
            });
        }

        return services;
    }

    /// <summary>
    /// Parent-based: a span follows its parent, and a new root is kept at
    /// <see cref="TelemetryTraceSettings.SampleRatio"/>, so a replay's trace is whole or absent.
    /// </summary>
    public static Sampler CreateSampler(TelemetryTraceSettings traces)
    {
        double ratio = traces?.EffectiveSampleRatio ?? 1.0;
        Sampler root = ratio switch
        {
            >= 1.0 => new AlwaysOnSampler(),
            <= 0.0 => new AlwaysOffSampler(),
            _ => new TraceIdRatioBasedSampler(ratio),
        };
        return new ParentBasedSampler(root);
    }

    private static void Configure(
        OtlpExporterOptions exporter,
        Uri endpoint,
        TelemetryExportSettings export
    )
    {
        exporter.Endpoint = endpoint;
        exporter.Protocol = OtlpExportProtocol.Grpc;
        exporter.TimeoutMilliseconds = export.TimeoutMilliseconds;
    }

    /// <summary>The batch each signal holds in memory before it is sent (<c>OpenTelemetry:Export</c>).</summary>
    public static void ApplyBatch<T>(
        BatchExportProcessorOptions<T> options,
        TelemetryExportSettings export
    )
        where T : class
    {
        if (options == null)
        {
            return;
        }

        options.MaxQueueSize = export.EffectiveMaxQueueSize;
        options.MaxExportBatchSize = export.EffectiveMaxExportBatchSize;
        options.ScheduledDelayMilliseconds = export.ScheduledDelayMilliseconds;
        options.ExporterTimeoutMilliseconds = export.TimeoutMilliseconds;
    }

    public static ServiceProvider BuildHeroesReplayProvider(
        this IServiceCollection services,
        ServiceProviderOptions options = null
    )
    {
        // Error logs become the role's lastError in its service heartbeat. Inert outside a role.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, ServiceHeartbeatLoggerProvider>()
        );
        // A role launched by `services start` also keeps its own rolling log file.
        services.AddServiceRoleLog(ServiceRoleLog.RoleFromEnvironment());
        ServiceProvider provider =
            options == null
                ? services.BuildServiceProvider()
                : services.BuildServiceProvider(options);
        // Resolving these starts export. Without a generic host they stay idle otherwise.
        _ = provider.GetService<TracerProvider>();
        _ = provider.GetService<MeterProvider>();
        return provider;
    }
}
