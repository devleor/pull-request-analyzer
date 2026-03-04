using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PullRequestAnalyzer.Middleware;
using PullRequestAnalyzer.Services;
using StackExchange.Redis;

namespace PullRequestAnalyzer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        services.AddSingleton<RedisCacheService>();
        services.AddSingleton<JobQueueService>();
        services.AddSingleton<DistributedLockService>();
        services.AddSingleton<DiffChunkingService>();
        services.AddScoped<IGitHubService, GitHubIngestService>();
        services.AddScoped<IAnalysisService, SemanticKernelAnalysisService>();
        services.AddScoped<WebhookService>();
        services.AddHostedService<AnalysisBackgroundService>();

        return services;
    }

    public static IServiceCollection AddRedis(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConn = Environment.GetEnvironmentVariable("REDIS_URL")
            ?? configuration["Redis:ConnectionString"]
            ?? "localhost:6379";

        var redis = ConnectionMultiplexer.Connect(new ConfigurationOptions
        {
            EndPoints = { redisConn },
            AbortOnConnectFail = false,
            ConnectRetry = 3,
            ConnectTimeout = 5000,
            SyncTimeout = 5000,
            AsyncTimeout = 5000,
            KeepAlive = 60,
            AllowAdmin = false
        });

        services.AddSingleton<IConnectionMultiplexer>(redis);
        services.AddHealthChecks().AddRedis(redisConn, name: "redis", tags: new[] { "ready" });

        return services;
    }

    public static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(provider =>
        {
            return new RateLimitOptions
            {
                MaxRequests = configuration.GetValue<int>("RateLimiting:MaxRequestsPerWindow", 60),
                WindowSizeSeconds = configuration.GetValue<int>("RateLimiting:WindowSizeSeconds", 60),
                EndpointLimits = configuration.GetSection("RateLimiting:EndpointLimits")
                    .Get<Dictionary<string, EndpointLimit>>() ?? new()
            };
        });

        return services;
    }

    public static IServiceCollection AddTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var langfuseHost = Environment.GetEnvironmentVariable("LANGFUSE_HOST")
            ?? configuration["Langfuse:Host"];

        if (string.IsNullOrEmpty(langfuseHost))
            return services;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService("PullRequestAnalyzer", serviceVersion: "1.0.0")
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("environment",
                        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"),
                    new KeyValuePair<string, object>("release",
                        Environment.GetEnvironmentVariable("LANGFUSE_RELEASE") ?? "local"),
                }))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("PullRequestAnalyzer")
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri($"{langfuseHost}/api/public/otel/v1/traces");
                        options.Protocol = OtlpExportProtocol.HttpProtobuf;
                        options.Headers = $"x-langfuse-public-key={Environment.GetEnvironmentVariable("LANGFUSE_PUBLIC_KEY")}," +
                                        $"x-langfuse-secret-key={Environment.GetEnvironmentVariable("LANGFUSE_SECRET_KEY")}";
                    });
            });

        return services;
    }
}