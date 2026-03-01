using System.Text;
using System.Text.Json;

namespace PullRequestAnalyzer.Services;

public sealed record LangfuseTrace(
    string TraceId,
    string Name,
    string Input,
    string Output,
    string Model,
    int    LatencyMs,
    Dictionary<string, object> Metadata
);

public sealed class LangfuseService
{
    private readonly HttpClient? _http;
    private readonly bool       _enabled;
    private readonly ILogger<LangfuseService> _logger;

    public LangfuseService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<LangfuseService> logger)
    {
        _logger = logger;

        var host      = config["Langfuse:Host"] ?? Environment.GetEnvironmentVariable("LANGFUSE_HOST");
        var publicKey = config["Langfuse:PublicKey"] ?? Environment.GetEnvironmentVariable("LANGFUSE_PUBLIC_KEY");
        var secretKey = config["Langfuse:SecretKey"] ?? Environment.GetEnvironmentVariable("LANGFUSE_SECRET_KEY");

        _enabled = !string.IsNullOrEmpty(host)
                   && !string.IsNullOrEmpty(publicKey)
                   && !string.IsNullOrEmpty(secretKey);

        if (!_enabled)
        {
            _logger.LogInformation("Langfuse tracing is disabled — set LANGFUSE_HOST, LANGFUSE_PUBLIC_KEY and LANGFUSE_SECRET_KEY to enable");
            return;
        }

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{publicKey}:{secretKey}"));

        _http = httpFactory.CreateClient("langfuse");
        _http.BaseAddress = new Uri(host!.TrimEnd('/'));
        _http.DefaultRequestHeaders.Add("Authorization", $"Basic {credentials}");
    }

    public async Task TraceAsync(LangfuseTrace trace)
    {
        if (!_enabled) return;

        try
        {
            var events = new List<object>
            {
                new
                {
                    id        = trace.TraceId,
                    type      = "trace-create",
                    timestamp = DateTime.UtcNow.ToString("o"),
                    body      = new
                    {
                        id       = trace.TraceId,
                        name     = trace.Name,
                        input    = trace.Input,
                        output   = trace.Output,
                        metadata = trace.Metadata
                    }
                },
                new
                {
                    id        = Guid.NewGuid().ToString(),
                    type      = "generation-create",
                    timestamp = DateTime.UtcNow.ToString("o"),
                    body      = new
                    {
                        traceId  = trace.TraceId,
                        name     = "llm-call",
                        model    = trace.Model,
                        input    = trace.Input,
                        output   = trace.Output,
                        latency  = trace.LatencyMs,
                        metadata = trace.Metadata
                    }
                }
            };

            var body = JsonSerializer.Serialize(new { batch = events });

            var response = await _http!.PostAsync(
                "/api/public/ingestion",
                new StringContent(body, Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Langfuse ingestion returned {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send trace to Langfuse — analysis result is unaffected");
        }
    }
}
