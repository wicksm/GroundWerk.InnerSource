using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace InnerSource.Telemetry.Tests;

public partial class TelemetryExtensionsTests
{
    private static readonly CancellationToken CancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task TracesRequestsButNotHealthChecks()
    {
        var activities = new List<Activity>();
        var builder = CreateBuilder();
        builder.AddTelemetry();
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(activities));
        builder.Services.AddHealthChecks();

        await using (var app = builder.Build())
        {
            app.MapHealthChecks("/health");
            app.MapGet("/orders", () => "[]");
            await app.StartAsync(CancellationToken);

            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            (await client.GetAsync(new Uri("/health", UriKind.Relative), CancellationToken)).EnsureSuccessStatusCode();
            (await client.GetAsync(new Uri("/orders", UriKind.Relative), CancellationToken)).EnsureSuccessStatusCode();

            await app.StopAsync(CancellationToken);
        }

        var requests = activities.Where(activity => activity.Kind == ActivityKind.Server).Select(activity => activity.GetTagItem("url.path")).ToList();
        Assert.Equal(["/orders"], requests);
    }

    [Fact]
    public async Task ExportsTracesMetricsAndLogsToTheConfiguredEndpoint()
    {
        var exports = new ConcurrentBag<(string Path, string ApiKey)>();
        var receiverBuilder = CreateBuilder();
        await using var receiver = receiverBuilder.Build();
        receiver.MapPost("/v1/{signal}", (HttpRequest request) =>
        {
            exports.Add((request.Path.Value!, request.Headers["x-otlp-api-key"].ToString()));
            return Results.Ok();
        });
        await receiver.StartAsync(CancellationToken);

        var builder = CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = receiver.Urls.First(),
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_EXPORTER_OTLP_HEADERS"] = "x-otlp-api-key=test-key",
        });
        builder.AddTelemetry();

        await using (var app = builder.Build())
        {
            app.MapGet("/orders", () => "[]");
            await app.StartAsync(CancellationToken);

            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            (await client.GetAsync(new Uri("/orders", UriKind.Relative), CancellationToken)).EnsureSuccessStatusCode();

            // Stopping the app flushes all three signals to the receiver
            await app.StopAsync(CancellationToken);
        }

        Assert.Equal(["/v1/logs", "/v1/metrics", "/v1/traces"], exports.Select(export => export.Path).Distinct().Order());
        Assert.All(exports, export => Assert.Equal("test-key", export.ApiKey));
    }

    [Fact]
    public void LogsKeepTheirMessageAndFields()
    {
        var records = new List<LogRecord>();
        var builder = Host.CreateApplicationBuilder();
        builder.AddTelemetry();
        builder.Services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddInMemoryExporter(records));

        using (var host = builder.Build())
        {
            var logger = host.Services.GetRequiredService<ILogger<TelemetryExtensionsTests>>();
            OrderChanged(logger, "V-1001");
        }

        var record = Assert.Single(records, record => record.EventId.Name == nameof(OrderChanged));
        Assert.Equal("Order V-1001 changed", record.FormattedMessage);
        Assert.Contains(new KeyValuePair<string, object?>("OrderId", "V-1001"), record.Attributes!);
    }

    [Fact]
    public void NamesTheServiceAfterTheApplication()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ApplicationName = "Orders.Api" });
        builder.AddTelemetry();

        using var host = builder.Build();
        var resource = host.Services.GetRequiredService<TracerProvider>().GetResource();

        Assert.Contains(new KeyValuePair<string, object>("service.name", "Orders.Api"), resource.Attributes);
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} changed")]
    private static partial void OrderChanged(ILogger logger, string orderId);
}
