using StackExchange.Redis;
using PullRequestAnalyzer.Services;
using PullRequestAnalyzer.Middleware;
using Serilog;
using Serilog.Events;
using OpenTelemetry;

// Configure Serilog for structured logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .Enrich.WithProperty("Application", "PullRequestAnalyzer")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting Pull Request Analyzer API");

    var builder = WebApplication.CreateBuilder(args);

    // Use Serilog
    builder.Host.UseSerilog();

    builder.Configuration
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
        .AddEnvironmentVariables();

    // Configure OpenTelemetry for Langfuse
    TelemetryService.ConfigureOpenTelemetry(builder);

    // Add services
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new()
        {
            Title = "Pull Request Analyzer",
            Version = "v2.0",
            Description = "Production-ready PR analysis with LLM observability"
        });
    });

    // CORS
    builder.Services.AddCors(o =>
        o.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

    // HTTP Clients
    builder.Services.AddHttpClient("openrouter", c =>
    {
        c.Timeout = TimeSpan.FromSeconds(120);
        c.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/devleor/pull-request-analyzer");
    });

    builder.Services.AddHttpClient("webhook", c =>
        c.Timeout = TimeSpan.FromSeconds(30));

    // Redis Configuration
    var redisConn = Environment.GetEnvironmentVariable("REDIS_URL")
        ?? builder.Configuration["Redis:ConnectionString"]
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

    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

    // Core Services
    builder.Services.AddSingleton<RedisCacheService>();
    builder.Services.AddSingleton<RedisJobQueue>();
    builder.Services.AddSingleton<RedLockService>();
    builder.Services.AddSingleton<DiffChunkingService>();

    // Prompt Template Service
    builder.Services.AddSingleton<IPromptTemplateService, PromptTemplateService>();

    // GitHub Service
    builder.Services.AddScoped<IGitHubService, GitHubIngestService>();

    // Analysis Service - Use OpenTelemetry version if Langfuse is configured
    var langfuseEnabled = !string.IsNullOrEmpty(builder.Configuration["LANGFUSE_PUBLIC_KEY"]) ||
                         !string.IsNullOrEmpty(builder.Configuration["Langfuse:PublicKey"]);

    if (langfuseEnabled)
    {
        builder.Services.AddScoped<IAnalysisService, SemanticKernelAnalysisServiceWithOtel>();
        Log.Information("Using OpenTelemetry-based analysis service with Langfuse export");
    }
    else
    {
        builder.Services.AddScoped<IAnalysisService, SemanticKernelAnalysisService>();
        Log.Warning("Langfuse not configured - using standard analysis service");
    }

    builder.Services.AddScoped<WebhookService>();

    // Background Worker
    builder.Services.AddHostedService<RedisBackgroundWorker>();

    // Rate Limiting Configuration
    builder.Services.AddSingleton(provider =>
    {
        var config = provider.GetRequiredService<IConfiguration>();
        return new RateLimitOptions
        {
            MaxRequests = config.GetValue<int>("RateLimiting:MaxRequestsPerWindow", 60),
            WindowSizeSeconds = config.GetValue<int>("RateLimiting:WindowSizeSeconds", 60),
            EndpointLimits = config.GetSection("RateLimiting:EndpointLimits")
                .Get<Dictionary<string, EndpointLimit>>() ?? new()
        };
    });

    // Health Checks
    builder.Services.AddHealthChecks()
        .AddRedis(redisConn, name: "redis", tags: new[] { "ready" });

    var app = builder.Build();

    // Middleware Pipeline (order matters!)
    app.UseMiddleware<GlobalErrorHandlingMiddleware>();

    if (builder.Configuration.GetValue<bool>("RateLimiting:Enabled", true))
    {
        app.UseMiddleware<RateLimitingMiddleware>();
    }

    // Request Logging
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.GetLevel = (httpContext, elapsed, ex) => ex != null
            ? LogEventLevel.Error
            : httpContext.Response.StatusCode >= 400
                ? LogEventLevel.Warning
                : LogEventLevel.Information;
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].ToString());
            diagnosticContext.Set("RemoteIP", httpContext.Connection.RemoteIpAddress?.ToString());
        };
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseCors("AllowAll");
    app.UseAuthorization();
    app.MapControllers();

    // Health Check Endpoints
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false // Always healthy if the app is running
    });

    app.MapGet("/health", async (IConnectionMultiplexer mux) =>
    {
        var latency = await mux.GetDatabase().PingAsync();
        var langfuseConfigured = !string.IsNullOrEmpty(app.Configuration["LANGFUSE_PUBLIC_KEY"]);

        return Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            redis = new { status = "connected", latency_ms = latency.TotalMilliseconds },
            langfuse = new { status = langfuseConfigured ? "configured_via_otel" : "not_configured" },
            environment = app.Environment.EnvironmentName
        });
    }).WithName("HealthCheck").WithOpenApi();

    app.MapGet("/info", () => Results.Ok(new
    {
        name = "Pull Request Analyzer",
        version = "2.0.0-production",
        features = new
        {
            cache = "Redis with TTL management",
            queue = "Redis Streams",
            locking = "RedLock.net distributed locking",
            worker = "Background job processing",
            llm = "Semantic Kernel with OpenRouter",
            observability = "Langfuse LLM tracking",
            prompt_templates = "Redis-backed versioning",
            rate_limiting = "Sliding window algorithm",
            error_handling = "Global exception middleware",
            logging = "Structured logging with Serilog"
        },
        endpoints = new[]
        {
            "/api/pull-requests/{owner}/{repo}/{number}",
            "/api/pull-requests/{owner}/{repo}/{number}/commits",
            "/api/analyze",
            "/api/prompttemplate",
            "/health",
            "/swagger"
        }
    })).WithName("Info").WithOpenApi();

    // Initialize default prompt templates on startup (optional)
    _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(5000); // Wait for services to be ready
            using var scope = app.Services.CreateScope();
            var promptService = scope.ServiceProvider.GetRequiredService<IPromptTemplateService>();

            // Check if templates exist
            var systemPrompt = await promptService.GetPromptTemplateAsync("pr_analysis_system");
            if (systemPrompt == null)
            {
                Log.Information("Initializing default prompt templates");

                // Initialize using the controller endpoint
                var httpClient = new HttpClient();
                await httpClient.PostAsync("http://localhost:5000/api/prompttemplate/initialize", null);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize default prompt templates");
        }
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}