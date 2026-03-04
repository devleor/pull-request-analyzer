using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PullRequestAnalyzer.Configuration;
using PullRequestAnalyzer.Middleware;
using PullRequestAnalyzer.Services;
using StackExchange.Redis;

namespace PullRequestAnalyzer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        // Core Services
        services.AddSingleton<RedisCacheService>();
        services.AddSingleton<JobQueueService>();
        services.AddSingleton<DistributedLockService>();
        services.AddSingleton<DiffChunkingService>();

        // Analysis Services - now properly separated
        services.AddScoped<IPromptService, PromptService>();
        services.AddScoped<IJsonParsingService, JsonParsingService>();
        services.AddScoped<IValidationService, ValidationService>();
        services.AddScoped<IAnalysisService, SemanticKernelAnalysisServiceRefactored>();

        // Integration Services
        services.AddScoped<IGitHubService, GitHubIngestService>();
        services.AddScoped<WebhookService>();

        // Background Services
        services.AddHostedService<AnalysisBackgroundService>();

        // Configuration
        services.Configure<AnalysisConfiguration>(
            services.BuildServiceProvider()
                .GetRequiredService<IConfiguration>()
                .GetSection("Analysis"));

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
        var langfuseHost = Environment.GetEnvironmentVariable("LANGFUSE_BASE_URL")
            ?? Environment.GetEnvironmentVariable("LANGFUSE_HOST")
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
                    .AddSource("PullRequestAnalyzer*")
                    .SetSampler(new AlwaysOnSampler())
                    .AddOtlpExporter(options =>
                    {
                        var publicKey = Environment.GetEnvironmentVariable("LANGFUSE_PUBLIC_KEY");
                        var secretKey = Environment.GetEnvironmentVariable("LANGFUSE_SECRET_KEY");
                        var credentials = Convert.ToBase64String(
                            System.Text.Encoding.UTF8.GetBytes($"{publicKey}:{secretKey}"));

                        options.Endpoint = new Uri($"{langfuseHost}/api/public/otel/v1/traces");
                        options.Protocol = OtlpExportProtocol.HttpProtobuf;
                        options.Headers = $"Authorization=Basic {credentials}";
                    });
            });

        return services;
    }
}