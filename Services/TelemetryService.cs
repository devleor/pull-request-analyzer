using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PullRequestAnalyzer.Services;

/// <summary>
/// Telemetry service for OpenTelemetry integration with Langfuse
/// </summary>
public static class TelemetryService
{
    public static readonly string ServiceName = "PullRequestAnalyzer";
    public static readonly string ServiceVersion = "1.0.0";

    public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);

    public static void ConfigureOpenTelemetry(WebApplicationBuilder builder)
    {
        var langfuseEndpoint = builder.Configuration["LANGFUSE_OTEL_ENDPOINT"]
            ?? builder.Configuration["Langfuse:OtelEndpoint"]
            ?? "https://us.cloud.langfuse.com/api/public/otel";

        var langfusePublicKey = builder.Configuration["LANGFUSE_PUBLIC_KEY"]
            ?? builder.Configuration["Langfuse:PublicKey"];

        var langfuseSecretKey = builder.Configuration["LANGFUSE_SECRET_KEY"]
            ?? builder.Configuration["Langfuse:SecretKey"];

        if (string.IsNullOrEmpty(langfusePublicKey) || string.IsNullOrEmpty(langfuseSecretKey))
        {
            Console.WriteLine("Langfuse not configured - OpenTelemetry will run without exporting");
            ConfigureLocalOnlyTelemetry(builder);
            return;
        }

        // Configure OpenTelemetry with Langfuse export
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: ServiceName,
                    serviceVersion: ServiceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = builder.Environment.EnvironmentName,
                    ["service.instance.id"] = Environment.MachineName
                }))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(ServiceName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = (httpContext) =>
                        {
                            // Skip health checks and swagger
                            var path = httpContext.Request.Path.Value ?? "";
                            return !path.Contains("/health") && !path.Contains("/swagger");
                        };
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.FilterHttpRequestMessage = (httpRequestMessage) =>
                        {
                            // Don't trace calls to Langfuse itself
                            var uri = httpRequestMessage.RequestUri?.ToString() ?? "";
                            return !uri.Contains("langfuse.com");
                        };
                    });

                // Add OTLP exporter for Langfuse
                tracing.AddOtlpExporter(options =>
                {
                    // Use signal-specific endpoint for traces
                    var tracesEndpoint = langfuseEndpoint.TrimEnd('/') + "/v1/traces";
                    options.Endpoint = new Uri(tracesEndpoint);
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;

                    // Add authentication headers for Langfuse
                    var authToken = Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes($"{langfusePublicKey}:{langfuseSecretKey}"));

                    options.Headers = $"Authorization=Basic {authToken}";
                    options.ExportProcessorType = ExportProcessorType.Batch;
                    options.BatchExportProcessorOptions = new BatchExportProcessorOptions<Activity>
                    {
                        MaxQueueSize = 2048,
                        ScheduledDelayMilliseconds = 5000,
                        ExporterTimeoutMilliseconds = 30000,
                        MaxExportBatchSize = 512
                    };
                });

                // Console exporter removed - use Langfuse dashboard for debugging
            });

        var finalEndpoint = langfuseEndpoint.TrimEnd('/') + "/v1/traces";
        Console.WriteLine($"OpenTelemetry configured with Langfuse export to: {finalEndpoint}");
    }

    private static void ConfigureLocalOnlyTelemetry(WebApplicationBuilder builder)
    {
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: ServiceName,
                    serviceVersion: ServiceVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(ServiceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            });
    }

    /// <summary>
    /// Create a new activity (span) for tracking operations
    /// </summary>
    public static Activity? StartActivity(
        string operationName,
        ActivityKind kind = ActivityKind.Internal,
        Dictionary<string, object?>? tags = null)
    {
        var activity = ActivitySource.StartActivity(operationName, kind);

        if (activity != null && tags != null)
        {
            foreach (var tag in tags)
            {
                activity.SetTag(tag.Key, tag.Value);
            }
        }

        return activity;
    }

    /// <summary>
    /// Add an event to the current activity
    /// </summary>
    public static void AddEvent(string name, Dictionary<string, object?>? attributes = null)
    {
        var activity = Activity.Current;
        if (activity != null)
        {
            if (attributes != null)
            {
                var activityTags = new ActivityTagsCollection();
                foreach (var kvp in attributes)
                {
                    activityTags.Add(kvp.Key, kvp.Value);
                }
                activity.AddEvent(new ActivityEvent(name, tags: activityTags));
            }
            else
            {
                activity.AddEvent(new ActivityEvent(name));
            }
        }
    }

    /// <summary>
    /// Record an exception in the current activity
    /// </summary>
    public static void RecordException(Exception ex)
    {
        var activity = Activity.Current;
        activity?.AddException(ex);
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    }
}