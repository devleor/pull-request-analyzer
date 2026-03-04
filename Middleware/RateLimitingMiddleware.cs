using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace PullRequestAnalyzer.Middleware;

/// <summary>
/// Production-ready rate limiting middleware with sliding window algorithm
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly RateLimitOptions _options;
    private readonly ConcurrentDictionary<string, SlidingWindow> _windows;

    public RateLimitingMiddleware(
        RequestDelegate next,
        ILogger<RateLimitingMiddleware> logger,
        RateLimitOptions options)
    {
        _next = next;
        _logger = logger;
        _options = options;
        _windows = new ConcurrentDictionary<string, SlidingWindow>();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip rate limiting for health checks and internal endpoints
        if (context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        var clientId = GetClientIdentifier(context);
        var window = _windows.GetOrAdd(clientId, _ => new SlidingWindow(_options.WindowSizeSeconds));

        // Clean up old windows periodically
        if (_windows.Count > 1000)
        {
            CleanupOldWindows();
        }

        var (allowed, retryAfter) = window.TryAddRequest(_options.MaxRequests);

        // Add rate limit headers
        context.Response.Headers["X-RateLimit-Limit"] = _options.MaxRequests.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, _options.MaxRequests - window.CurrentCount).ToString();
        context.Response.Headers["X-RateLimit-Reset"] = window.WindowResetTime.ToString();

        if (!allowed)
        {
            context.Response.Headers["Retry-After"] = retryAfter.ToString();

            _logger.LogWarning("Rate limit exceeded for client {ClientId} - IP: {IP}, Path: {Path}",
                clientId,
                context.Connection.RemoteIpAddress,
                context.Request.Path);

            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.ContentType = "application/json";

            var response = new
            {
                error = "rate_limit_exceeded",
                message = $"Too many requests. Please retry after {retryAfter} seconds.",
                retry_after = retryAfter,
                limit = _options.MaxRequests,
                window = _options.WindowSizeSeconds
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
            return;
        }

        await _next(context);
    }

    private string GetClientIdentifier(HttpContext context)
    {
        // Try to get API key first
        if (context.Request.Headers.TryGetValue("X-API-Key", out var apiKey) && !string.IsNullOrEmpty(apiKey))
        {
            return $"apikey:{apiKey}";
        }

        // Fall back to IP address
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}";
    }

    private void CleanupOldWindows()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
        var keysToRemove = _windows
            .Where(kvp => kvp.Value.LastAccessTime < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _windows.TryRemove(key, out _);
        }

        if (keysToRemove.Any())
        {
            _logger.LogDebug("Cleaned up {Count} old rate limit windows", keysToRemove.Count);
        }
    }
}

public class RateLimitOptions
{
    public int MaxRequests { get; set; } = 60;
    public int WindowSizeSeconds { get; set; } = 60;

    // Different limits for different endpoints
    public Dictionary<string, EndpointLimit> EndpointLimits { get; set; } = new()
    {
        ["/api/analyze"] = new EndpointLimit { MaxRequests = 10, WindowSizeSeconds = 60 },
        ["/api/pull-requests"] = new EndpointLimit { MaxRequests = 100, WindowSizeSeconds = 60 }
    };
}

public class EndpointLimit
{
    public int MaxRequests { get; set; }
    public int WindowSizeSeconds { get; set; }
}

/// <summary>
/// Thread-safe sliding window implementation
/// </summary>
internal class SlidingWindow
{
    private readonly object _lock = new();
    private readonly Queue<DateTimeOffset> _requests = new();
    private readonly int _windowSizeSeconds;

    public DateTimeOffset LastAccessTime { get; private set; }
    public int CurrentCount => _requests.Count;
    public DateTimeOffset WindowResetTime => _requests.Any()
        ? _requests.Peek().AddSeconds(_windowSizeSeconds)
        : DateTimeOffset.UtcNow.AddSeconds(_windowSizeSeconds);

    public SlidingWindow(int windowSizeSeconds)
    {
        _windowSizeSeconds = windowSizeSeconds;
        LastAccessTime = DateTimeOffset.UtcNow;
    }

    public (bool Allowed, int RetryAfterSeconds) TryAddRequest(int maxRequests)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            LastAccessTime = now;
            var windowStart = now.AddSeconds(-_windowSizeSeconds);

            // Remove expired requests
            while (_requests.Any() && _requests.Peek() < windowStart)
            {
                _requests.Dequeue();
            }

            if (_requests.Count >= maxRequests)
            {
                var oldestRequest = _requests.Peek();
                var retryAfter = (int)Math.Ceiling((oldestRequest.AddSeconds(_windowSizeSeconds) - now).TotalSeconds);
                return (false, retryAfter);
            }

            _requests.Enqueue(now);
            return (true, 0);
        }
    }
}