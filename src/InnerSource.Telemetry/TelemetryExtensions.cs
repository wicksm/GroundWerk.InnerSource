using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace InnerSource.Telemetry;

/// <summary>
/// Sets up OpenTelemetry the same way in every service, so changing where telemetry goes is a
/// configuration change and the instrumentation stays as it is.
/// </summary>
public static class TelemetryExtensions
{
    // Health probes call this path every few seconds, so their requests would bury real traces
    private const string HealthPath = "/health";

    /// <summary>
    /// Adds OpenTelemetry traces, metrics and structured logs for ASP.NET Core and HttpClient, and
    /// names the service after the application unless configuration sets OTEL_SERVICE_NAME. When
    /// configuration sets OTEL_EXPORTER_OTLP_ENDPOINT, all three are exported over OTLP, with the
    /// protocol and headers from the standard OTEL_ settings in configuration.
    /// </summary>
    /// <typeparam name="TBuilder">The application's builder, such as a WebApplicationBuilder.</typeparam>
    /// <param name="builder">The application's builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TBuilder AddTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var telemetry = builder.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context => !context.Request.Path.StartsWithSegments(HealthPath))
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.UseOtlpExporter();
        }

        return builder;
    }
}
