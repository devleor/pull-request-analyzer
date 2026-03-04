using PullRequestAnalyzer.Extensions;
using PullRequestAnalyzer.Middleware;
using Serilog;
using Serilog.Events;

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

    builder.Host.UseSerilog();

    builder.Configuration
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
        .AddEnvironmentVariables();

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

    builder.Services.AddCors(o =>
        o.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

    builder.Services.AddHttpClient("openrouter", c =>
    {
        c.Timeout = TimeSpan.FromSeconds(120);
        c.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/devleor/pull-request-analyzer");
    });

    builder.Services.AddHttpClient("webhook", c =>
        c.Timeout = TimeSpan.FromSeconds(30));

    builder.Services.AddRedis(builder.Configuration);
    builder.Services.AddDomainServices();
    builder.Services.AddRateLimiting(builder.Configuration);
    builder.Services.AddTelemetry(builder.Configuration);

    var app = builder.Build();

    app.UseMiddleware<GlobalErrorHandlingMiddleware>();

    if (builder.Configuration.GetValue<bool>("RateLimiting:Enabled", true))
    {
        app.UseMiddleware<RateLimitingMiddleware>();
    }

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

    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false
    });

    app.MapGet("/health", async (StackExchange.Redis.IConnectionMultiplexer mux) =>
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
            rate_limiting = "Sliding window algorithm",
            error_handling = "Global exception middleware",
            logging = "Structured logging with Serilog"
        },
        endpoints = new[]
        {
            "/api/pull-requests/{owner}/{repo}/{number}",
            "/api/pull-requests/{owner}/{repo}/{number}/commits",
            "/api/analyze",
            "/health",
            "/swagger"
        }
    })).WithName("Info").WithOpenApi();

    _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(5000);
            using var scope = app.Services.CreateScope();
            var redis = scope.ServiceProvider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>();
            var db = redis.GetDatabase();

            var systemPrompt = await db.StringGetAsync("prompt:pr_analysis_system");
            if (systemPrompt.IsNullOrEmpty)
            {
                Log.Information("Initializing default system prompt");
                await db.StringSetAsync("prompt:pr_analysis_system", @"You are an expert code reviewer analyzing a GitHub pull request. Provide a structured JSON analysis following this exact schema:

{
  ""executive_summary"": [""2-6 bullet points summarizing key changes""],
  ""change_units"": [
    {
      ""type"": ""feature|bugfix|refactor|test|docs|performance|security|style"",
      ""title"": ""Short descriptive title"",
      ""description"": ""What changed"",
      ""inferred_intent"": ""Why it likely changed"",
      ""confidence_level"": ""high|medium|low"",
      ""rationale"": ""Explanation for confidence level"",
      ""evidence"": ""Specific quote from diff"",
      ""affected_files"": [""MUST list the actual file paths from the PR""],
      ""test_coverage_signal"": ""tests_added|tests_modified|no_tests""
    }
  ],
  ""risks_and_concerns"": [""List of identified risks""],
  ""claimed_vs_actual"": {
    ""alignment_assessment"": ""aligned|partially_aligned|misaligned"",
    ""discrepancies"": [""List of discrepancies if any""]
  }
}

IMPORTANT Rules:
1. Base analysis on actual diffs, not just commit messages
2. Every claim must include evidence from the diff
3. Set confidence levels honestly based on evidence strength
4. Identify ALL significant changes across ALL files
5. For each change_unit, MUST populate affected_files with the actual file paths shown in the PR (files are listed in ""Files Changed in this PR"" section)
6. Extract file paths from the file list provided at the beginning of the PR data
7. Flag any potential risks or concerns");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize default system prompt");
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