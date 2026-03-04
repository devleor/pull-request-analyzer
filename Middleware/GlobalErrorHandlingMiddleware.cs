using System.Net;
using System.Text.Json;

namespace PullRequestAnalyzer.Middleware;

/// <summary>
/// Global error handling middleware for production-ready error responses
/// </summary>
public class GlobalErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalErrorHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalErrorHandlingMiddleware(
        RequestDelegate next,
        ILogger<GlobalErrorHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.TraceIdentifier;

        // Log the error with structured logging
        LogException(exception, correlationId, context);

        // Determine response based on exception type
        var (statusCode, errorResponse) = exception switch
        {
            ArgumentNullException _ => (
                HttpStatusCode.BadRequest,
                CreateErrorResponse("validation_error", "Required parameter is missing", correlationId)
            ),
            ArgumentException argEx => (
                HttpStatusCode.BadRequest,
                CreateErrorResponse("validation_error", argEx.Message, correlationId)
            ),
            UnauthorizedAccessException _ => (
                HttpStatusCode.Unauthorized,
                CreateErrorResponse("unauthorized", "Authentication required", correlationId)
            ),
            KeyNotFoundException _ => (
                HttpStatusCode.NotFound,
                CreateErrorResponse("not_found", "Resource not found", correlationId)
            ),
            TimeoutException _ => (
                HttpStatusCode.RequestTimeout,
                CreateErrorResponse("timeout", "Request timed out", correlationId)
            ),
            HttpRequestException httpEx when httpEx.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) => (
                HttpStatusCode.TooManyRequests,
                CreateErrorResponse("upstream_rate_limit", "Upstream service rate limit exceeded", correlationId)
            ),
            InvalidOperationException invOp when invOp.Message.Contains("LLM", StringComparison.OrdinalIgnoreCase) => (
                HttpStatusCode.ServiceUnavailable,
                CreateErrorResponse("llm_error", "AI service temporarily unavailable", correlationId)
            ),
            _ => (
                HttpStatusCode.InternalServerError,
                CreateErrorResponse("internal_error", "An unexpected error occurred", correlationId)
            )
        };

        // Add error details in development
        if (_environment.IsDevelopment() && statusCode == HttpStatusCode.InternalServerError)
        {
            errorResponse.Details = new
            {
                exception = exception.GetType().Name,
                message = exception.Message,
                stackTrace = exception.StackTrace
            };
        }

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse, options));
    }

    private void LogException(Exception exception, string correlationId, HttpContext context)
    {
        var logLevel = exception switch
        {
            ArgumentException _ => LogLevel.Warning,
            UnauthorizedAccessException _ => LogLevel.Warning,
            KeyNotFoundException _ => LogLevel.Information,
            _ => LogLevel.Error
        };

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["RequestPath"] = context.Request.Path.Value ?? "unknown",
            ["RequestMethod"] = context.Request.Method,
            ["UserAgent"] = context.Request.Headers["User-Agent"].ToString(),
            ["RemoteIP"] = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
        });

        _logger.Log(
            logLevel,
            exception,
            "Unhandled exception occurred - Type: {ExceptionType}, Message: {Message}",
            exception.GetType().Name,
            exception.Message
        );
    }

    private ErrorResponse CreateErrorResponse(string code, string message, string correlationId)
    {
        return new ErrorResponse
        {
            Error = new ErrorDetails
            {
                Code = code,
                Message = message,
                CorrelationId = correlationId,
                Timestamp = DateTimeOffset.UtcNow
            }
        };
    }
}

public class ErrorResponse
{
    public ErrorDetails Error { get; set; } = new();
    public object? Details { get; set; }
}

public class ErrorDetails
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}