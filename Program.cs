using MassTransit;
using PullRequestAnalyzer.Consumers;
using PullRequestAnalyzer.Messages;
using PullRequestAnalyzer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register custom services
builder.Services.AddScoped<LLMAnalysisService>();
builder.Services.AddScoped<JobStatusService>();
builder.Services.AddScoped<WebhookService>();

// Register background worker service
builder.Services.AddHostedService<BackgroundWorkerService>();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

// Add configuration from appsettings.json
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

// Configure MassTransit with RabbitMQ
var rabbitMqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
var rabbitMqPort = int.Parse(builder.Configuration["RabbitMQ:Port"] ?? "5672");
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

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

// Add a health check endpoint
app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow })
    .WithName("HealthCheck")
    .WithOpenApi();

// Add an info endpoint
app.MapGet("/info", () => new 
{ 
    name = "Pull Request Analyzer API",
    version = "2.0.0",
    description = "Analyzes GitHub pull requests using LLM with async support via MassTransit",
    endpoints = new
    {
        v1 = new
        {
            pullRequests = "/api/pull-requests/{owner}/{repo}/{number}",
            commits = "/api/pull-requests/{owner}/{repo}/{number}/commits",
            analyze = "/api/analyze"
        },
        v2 = new
        {
            analyzeAsync = "/api/v2/analyze-async",
            jobStatus = "/api/v2/jobs/{jobId}",
            listJobs = "/api/v2/jobs"
        },
        health = "/health"
    }
})
.WithName("Info")
.WithOpenApi();

Console.WriteLine("\n╔════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  Pull Request Analyzer API + Background Worker              ║");
Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");
Console.WriteLine($"Environment: {app.Environment.EnvironmentName}");
Console.WriteLine($"RabbitMQ: {rabbitMqHost}:{rabbitMqPort}");
Console.WriteLine("\n📡 Services:");
Console.WriteLine("  ✓ API Server: http://localhost:5000");
Console.WriteLine("  ✓ Swagger UI: http://localhost:5000/swagger");
Console.WriteLine("  ✓ Health Check: http://localhost:5000/health");
Console.WriteLine("  ✓ Background Worker: Processing messages from RabbitMQ");
Console.WriteLine("  ✓ RabbitMQ Management: http://localhost:15672 (guest/guest)\n");
Console.WriteLine("Press Ctrl+C to stop the application\n");

app.Run();
