using System;
using HeroesReplay.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HeroesReplay.CLI;

public static class HeroesReplayOpenTelemetry
{
    public const string DefaultOtlpEndpoint = "http://127.0.0.1:4317";
    public const string DefaultServiceName = "heroesreplay";

    public static IServiceCollection AddHeroesReplayOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        bool enabled = configuration.GetValue("OpenTelemetry:Enabled", true);
        if (!enabled)
        {
            return services;
        }

        string endpoint =
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? configuration["OpenTelemetry:OtlpEndpoint"]
            ?? DefaultOtlpEndpoint;

        string serviceName =
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
            ?? configuration["OpenTelemetry:ServiceName"]
            ?? DefaultServiceName;

        Uri otlpUri = new(endpoint);

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource =>
                resource.AddService(serviceName: serviceName, serviceNamespace: "HeroesReplay")
            )
            .WithTracing(tracing =>
            {
                tracing.AddSource(HeroesReplayTelemetry.SourceName);
                tracing.AddHttpClientInstrumentation();
                tracing.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = otlpUri;
                    exporter.Protocol = OtlpExportProtocol.Grpc;
                });
            })
            .WithMetrics(metrics =>
            {
                metrics.AddRuntimeInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = otlpUri;
                    exporter.Protocol = OtlpExportProtocol.Grpc;
                });
            });

        services.AddLogging(logging =>
        {
            logging.AddOpenTelemetry(otel =>
            {
                otel.IncludeFormattedMessage = true;
                otel.IncludeScopes = true;
                otel.ParseStateValues = true;
                otel.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = otlpUri;
                    exporter.Protocol = OtlpExportProtocol.Grpc;
                });
            });
        });

        return services;
    }

    public static ServiceProvider BuildHeroesReplayProvider(this IServiceCollection services)
    {
        ServiceProvider provider = services.BuildServiceProvider();
        _ = provider.GetService<TracerProvider>();
        _ = provider.GetService<MeterProvider>();
        return provider;
    }
}
