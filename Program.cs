using MassTransit;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;
using PullRequestAnalyzer.Consumers;
using PullRequestAnalyzer.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// ---------------------------------------------------------------------------
// ASP.NET Core
// ---------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Pull Request Analyzer API", Version = "v1" });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// ---------------------------------------------------------------------------
// Redis — Connection Multiplexer (singleton, thread-safe)
// ---------------------------------------------------------------------------
var redisConnectionString = builder.Configuration["Redis:ConnectionString"]
    ?? builder.Configuration["REDIS_URL"]
    ?? "localhost:6379";

var redisMultiplexer = ConnectionMultiplexer.Connect(redisConnectionString);
builder.Services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);

// IDistributedCache backed by Redis (used by ASP.NET Core internals if needed)
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration       = redisConnectionString;
    options.InstanceName        = "pr-analyzer:";
});

// ---------------------------------------------------------------------------
// Application Services
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<RedisCacheService>();   // Cache: PR data + analysis results + job status
builder.Services.AddSingleton<RedisJobQueue>();        // Queue: Redis Streams
builder.Services.AddSingleton<RedLockService>();       // Distributed lock: RedLock algorithm
builder.Services.AddScoped<LLMAnalysisService>();      // LLM analysis via OpenRouter
builder.Services.AddScoped<WebhookService>();          // Webhook notifications
builder.Services.AddScoped<JobStatusService>();        // Legacy file-based status (kept for compat)

// ---------------------------------------------------------------------------
// Background Worker — Redis Streams consumer + RedLock
// ---------------------------------------------------------------------------
builder.Services.AddHostedService<RedisBackgroundWorker>();

// ---------------------------------------------------------------------------
// MassTransit + RabbitMQ — for event-driven sagas and pub/sub
// ---------------------------------------------------------------------------
var rabbitMqHost     = builder.Configuration["RabbitMQ:Host"]     ?? "localhost";
var rabbitMqPort     = int.Parse(builder.Configuration["RabbitMQ:Port"] ?? "5672");
var rabbitMqUsername = builder.Configuration["RabbitMQ:Username"] ?? "guest";
var rabbitMqPassword = builder.Configuration["RabbitMQ:Password"] ?? "guest";

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AnalyzePullRequestConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host($"rabbitmq://{rabbitMqHost}:{rabbitMqPort}", h =>
        {
            h.Username(rabbitMqUsername);
            h.Password(rabbitMqPassword);
        });

        cfg.ConfigureEndpoints(context);
    });
});

// ---------------------------------------------------------------------------
// Build App
// ---------------------------------------------------------------------------
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

// ---------------------------------------------------------------------------
// Minimal API Endpoints
// ---------------------------------------------------------------------------
app.MapGet("/health", async (IConnectionMultiplexer redis) =>
{
    var db        = redis.GetDatabase();
    var redisPing = await db.PingAsync();

    return new
    {
        status    = "healthy",
        timestamp = DateTime.UtcNow,
        redis     = new { status = "connected", latency_ms = redisPing.TotalMilliseconds },
        rabbitmq  = new { status = "connected", host = $"{rabbitMqHost}:{rabbitMqPort}" }
    };
})
.WithName("HealthCheck")
.WithOpenApi();

app.MapGet("/info", (RedisJobQueue queue) => new
{
    name        = "Pull Request Analyzer API",
    version     = "3.0.0",
    description = "Analyzes GitHub pull requests using LLM with Redis cache, Redis Streams queue, and RedLock",
    stack = new
    {
        cache    = "Redis (StackExchange.Redis)",
        queue    = "Redis Streams",
        locking  = "RedLock.net (distributed lock)",
        broker   = "RabbitMQ + MassTransit (pub/sub events)",
        llm      = "OpenRouter (BYOK)"
    },
    endpoints = new
    {
        v1 = new
        {
            pullRequests = "/api/pull-requests/{owner}/{repo}/{number}",
            commits      = "/api/pull-requests/{owner}/{repo}/{number}/commits",
            analyze      = "/api/analyze"
        },
        v2 = new
        {
            analyzeAsync = "/api/v2/analyze-async",
            jobStatus    = "/api/v2/jobs/{jobId}",
            listJobs     = "/api/v2/jobs"
        },
        health = "/health",
        info   = "/info"
    }
})
.WithName("Info")
.WithOpenApi();

// ---------------------------------------------------------------------------
// Startup Banner
// ---------------------------------------------------------------------------
Console.WriteLine("\n╔════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  Pull Request Analyzer  v3.0                               ║");
Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");
Console.WriteLine($"  Environment : {app.Environment.EnvironmentName}");
Console.WriteLine($"  Redis       : {redisConnectionString}");
Console.WriteLine($"  RabbitMQ    : {rabbitMqHost}:{rabbitMqPort}");
Console.WriteLine();
Console.WriteLine("  Services:");
Console.WriteLine("    ✓ API Server          → http://localhost:5000");
Console.WriteLine("    ✓ Swagger UI          → http://localhost:5000/swagger");
Console.WriteLine("    ✓ Health Check        → http://localhost:5000/health");
Console.WriteLine("    ✓ Redis Cache         → StackExchange.Redis");
Console.WriteLine("    ✓ Redis Queue         → Redis Streams (queue:analyze)");
Console.WriteLine("    ✓ Distributed Lock    → RedLock.net");
Console.WriteLine("    ✓ Background Worker   → RedisBackgroundWorker (IHostedService)");
Console.WriteLine("    ✓ RabbitMQ UI         → http://localhost:15672 (guest/guest)");
Console.WriteLine("    ✓ Redis Commander     → http://localhost:8081");
Console.WriteLine();
Console.WriteLine("  Press Ctrl+C to stop\n");

app.Run();
