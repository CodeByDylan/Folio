using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Folio.Api.Infrastructure;

/// <summary>Traces, metrics and logs.</summary>
internal static class Telemetry
{
    /// <summary>The meter name every Folio instrument is published under.</summary>
    public const string SourceName = "Folio";

    /// <summary>Adds OpenTelemetry and resilient outbound HTTP.</summary>
    /// <param name="builder">The host builder being configured.</param>
    /// <returns>The same builder.</returns>
    public static IHostApplicationBuilder AddTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        _ = builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(SourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            _ = builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        _ = builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        return builder;
    }
}
