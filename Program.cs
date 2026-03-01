using StackExchange.Redis;
using PullRequestAnalyzer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "Pull Request Analyzer", Version = "v1" }));

builder.Services.AddCors(o =>
    o.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

builder.Services.AddHttpClient("openrouter", c =>
{
    c.Timeout = TimeSpan.FromSeconds(120);
    c.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/devleor/pull-request-analyzer");
});

builder.Services.AddHttpClient("webhook", c =>
    c.Timeout = TimeSpan.FromSeconds(30));

var redisConn = Environment.GetEnvironmentVariable("REDIS_URL")
    ?? builder.Configuration["Redis:ConnectionString"]
    ?? "localhost:6379";

var redis = ConnectionMultiplexer.Connect(redisConn);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

builder.Services.AddSingleton<RedisCacheService>();
builder.Services.AddSingleton<RedisJobQueue>();
builder.Services.AddSingleton<RedLockService>();
builder.Services.AddSingleton<DiffChunkingService>();

builder.Services.AddScoped<IGitHubService, GitHubIngestService>();
builder.Services.AddScoped<IAnalysisService, LLMAnalysisService>();
builder.Services.AddScoped<WebhookService>();

builder.Services.AddHostedService<RedisBackgroundWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", async (IConnectionMultiplexer mux) =>
{
    var latency = await mux.GetDatabase().PingAsync();
    return Results.Ok(new
    {
        status    = "healthy",
        timestamp = DateTime.UtcNow,
        redis     = new { status = "connected", latency_ms = latency.TotalMilliseconds }
    });
}).WithName("HealthCheck").WithOpenApi();

app.MapGet("/info", () => Results.Ok(new
{
    name    = "Pull Request Analyzer",
    version = "4.0.0",
    stack   = new
    {
        cache   = "Redis (StackExchange.Redis)",
        queue   = "Redis Streams",
        locking = "RedLock.net",
        worker  = "IHostedService (RedisBackgroundWorker)",
        llm     = "OpenRouter BYOK"
    }
})).WithName("Info").WithOpenApi();

app.Run();
