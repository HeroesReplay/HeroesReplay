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

        bool enabled = configuration.GetValue("OpenTelemetry:Enabled", true);
        if (!enabled)
        {
            return services;
        }

        string endpoint =
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? configuration["OpenTelemetry:OtlpEndpoint"]
            ?? DefaultOtlpEndpoint;

        serviceName =
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
            ?? serviceName
            ?? configuration["OpenTelemetry:ServiceName"]
            ?? DefaultServiceName;

        Uri otlpUri = new(endpoint);
        ResourceBuilder resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(serviceName: serviceName, serviceNamespace: "HeroesReplay");

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
                otel.SetResourceBuilder(resourceBuilder);
                otel.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = otlpUri;
                    exporter.Protocol = OtlpExportProtocol.Grpc;
                });
            });
        });

        return services;
    }

    public static ServiceProvider BuildHeroesReplayProvider(
        this IServiceCollection services,
        ServiceProviderOptions options = null
    )
    {
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
