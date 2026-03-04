using Microsoft.AspNetCore.Mvc;
using PullRequestAnalyzer.Models;
using PullRequestAnalyzer.Services;
using PullRequestAnalyzer.Messages;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace PullRequestAnalyzer.Controllers;

/// <summary>
/// Analysis endpoint as required by OBJECTIVE.MD
/// Supports both synchronous and asynchronous (with webhook) modes
/// </summary>
[ApiController]
[Route("api")]
public sealed class AnalyzeController : ControllerBase
{
    private readonly IAnalysisService _analysisService;
    private readonly RedisCacheService _cache;
    private readonly ILogger<AnalyzeController> _logger;

    public AnalyzeController(
        IAnalysisService analysisService,
        RedisCacheService cache,
        ILogger<AnalyzeController> logger)
    {
        _analysisService = analysisService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/analyze - Analysis endpoint (as required by OBJECTIVE.MD)
    /// Accepts PR JSON and returns structured analysis
    /// Can work both synchronously (default) or asynchronously (with webhook_url)
    /// </summary>
    [HttpPost("analyze")]
    public async Task<ActionResult> Analyze([FromBody] AnalyzeRequest request)
    {
        // Get pull request data from the request
        var pullRequest = request?.PullRequestData;

        if (pullRequest == null)
        {
            return BadRequest(new { error = "Pull request data is required" });
        }

        // Validate required fields
        if (string.IsNullOrEmpty(pullRequest.Owner) ||
            string.IsNullOrEmpty(pullRequest.Repo) ||
            pullRequest.Number == 0)
        {
            return BadRequest(new { error = "Invalid pull request data: owner, repo, and number are required" });
        }

        // Check if webhook URL is provided (async mode)
        if (!string.IsNullOrEmpty(request?.WebhookUrl))
        {
            _logger.LogInformation("Webhook URL provided, switching to async mode for PR {Owner}/{Repo}#{Number}",
                pullRequest.Owner, pullRequest.Repo, pullRequest.Number);

            // Reuse the async infrastructure
            return await SubmitAsyncAnalysis(pullRequest, request.WebhookUrl);
        }

        try
        {
            _logger.LogInformation("Starting synchronous analysis for PR {Owner}/{Repo}#{Number}",
                pullRequest.Owner, pullRequest.Repo, pullRequest.Number);

            var stopwatch = Stopwatch.StartNew();

            // Perform the FULL analysis (always fresh, no caching)
            _logger.LogInformation("Performing full LLM analysis for PR {Owner}/{Repo}#{Number}",
                pullRequest.Owner, pullRequest.Repo, pullRequest.Number);

            var result = await _analysisService.AnalyzeAsync(pullRequest);

            stopwatch.Stop();
            _logger.LogInformation("Full analysis completed for PR {Owner}/{Repo}#{Number} in {ElapsedMs}ms - " +
                "Change units: {ChangeUnits}, Confidence: {Confidence}, Alignment: {Alignment}",
                pullRequest.Owner, pullRequest.Repo, pullRequest.Number, stopwatch.ElapsedMilliseconds,
                result.ChangeUnits.Count, result.ConfidenceScore, result.ClaimedVsActual.AlignmentAssessment);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during analysis");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing PR {Owner}/{Repo}#{Number}",
                pullRequest.Owner, pullRequest.Repo, pullRequest.Number);

            return StatusCode(500, new {
                error = "An error occurred during analysis",
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// GET /api/analyze/health - Check if analysis service is available
    /// </summary>
    [HttpGet("analyze/health")]
    public ActionResult<object> AnalyzeHealth()
    {
        return Ok(new
        {
            status = "healthy",
            service = "analysis",
            timestamp = DateTime.UtcNow,
            capabilities = new
            {
                synchronous = true,
                asynchronous = true,
                caching = true,
                llm_provider = Environment.GetEnvironmentVariable("OPENROUTER_MODEL") ?? "configured"
            }
        });
    }

    private async Task<ActionResult> SubmitAsyncAnalysis(PullRequestData pullRequest, string webhookUrl)
    {
        // Inject dependencies we need for async processing
        var queue = HttpContext.RequestServices.GetRequiredService<JobQueueService>();

        var command = new AnalyzePullRequestCommand
        {
            PullRequestData = pullRequest,
            WebhookUrl = webhookUrl
        };

        var jobData = new JobStatusRecord(
            command.JobId,
            "queued",
            command.CreatedAt,
            null, null,
            pullRequest.Number,
            null, null);

        await _cache.SetJobAsync(command.JobId, jobData);
        await queue.EnqueueAsync(command);

        _logger.LogInformation("Queued analysis job {JobId} for PR {Id} with webhook",
            command.JobId, pullRequest.ToIdentifier());

        return Accepted(new
        {
            job_id = command.JobId,
            status = "queued",
            message = "Analysis queued. Results will be sent to webhook URL.",
            webhook_url = webhookUrl,
            status_check_url = $"/api/v2/jobs/{command.JobId}"
        });
    }
}

/// <summary>
/// Request model for /api/analyze endpoint
/// Format: { "pull_request_data": {...}, "webhook_url": "optional" }
/// If webhook_url is provided, returns immediately and sends results to webhook
/// If no webhook_url, waits for analysis to complete (synchronous)
/// </summary>
public class AnalyzeRequest
{
    [JsonPropertyName("pull_request_data")]
    public PullRequestData? PullRequestData { get; set; }

    [JsonPropertyName("webhook_url")]
    public string? WebhookUrl { get; set; }
}