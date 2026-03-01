using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using PullRequestAnalyzer.Messages;
using PullRequestAnalyzer.Models;
using PullRequestAnalyzer.Services;

namespace PullRequestAnalyzer.Controllers;

[ApiController]
[Route("api/v2")]
public sealed class AsyncAnalysisController : ControllerBase
{
    private readonly RedisJobQueue   _queue;
    private readonly RedisCacheService _cache;
    private readonly ILogger<AsyncAnalysisController> _logger;

    public AsyncAnalysisController(
        RedisJobQueue queue,
        RedisCacheService cache,
        ILogger<AsyncAnalysisController> logger)
    {
        _queue  = queue;
        _cache  = cache;
        _logger = logger;
    }

    [HttpPost("analyze-async")]
    public async Task<ActionResult<AnalysisJobResponse>> SubmitAnalysis(
        [FromBody] AnalyzeAsyncRequest request)
    {
        if (request.PullRequestData is null)
            return BadRequest(new { error = "PR data is required" });

        var command = new AnalyzePullRequestCommand
        {
            PullRequestData = request.PullRequestData,
            WebhookUrl      = request.WebhookUrl
        };

        var jobData = new JobStatusRecord(
            command.JobId,
            "queued",
            command.CreatedAt,
            null, null,
            request.PullRequestData.Number,
            null, null);

        await _cache.SetJobAsync(command.JobId, jobData);
        await _queue.EnqueueAsync(command);

        _logger.LogInformation("Queued analysis job {JobId} for PR {Id}",
            command.JobId, request.PullRequestData.ToIdentifier());

        return Accepted(new AnalysisJobResponse(
            command.JobId,
            "queued",
            command.CreatedAt,
            $"/api/v2/jobs/{command.JobId}"));
    }

    [HttpGet("jobs/{jobId}")]
    public async Task<ActionResult<JobStatusRecord>> GetJob(string jobId)
    {
        var job = await _cache.GetJobAsync<JobStatusRecord>(jobId);

        return job is null
            ? NotFound(new { error = "Job not found" })
            : Ok(job);
    }

    [HttpGet("jobs")]
    public async Task<ActionResult<IEnumerable<JobStatusRecord>>> ListJobs()
    {
        var ids  = await _cache.GetAllJobIdsAsync();
        var jobs = new List<JobStatusRecord>();

        foreach (var id in ids)
        {
            var job = await _cache.GetJobAsync<JobStatusRecord>(id);
            if (job is not null) jobs.Add(job);
        }

        return Ok(jobs.OrderByDescending(j => j.CreatedAt));
    }
}

public sealed record AnalyzeAsyncRequest(
    [property: JsonPropertyName("pull_request_data")] PullRequestData? PullRequestData,
    [property: JsonPropertyName("webhook_url")]        string?          WebhookUrl
);

public sealed record AnalysisJobResponse(
    [property: JsonPropertyName("job_id")]         string   JobId,
    [property: JsonPropertyName("status")]         string   Status,
    [property: JsonPropertyName("created_at")]     DateTime CreatedAt,
    [property: JsonPropertyName("status_check_url")] string StatusCheckUrl
);

public sealed record JobStatusRecord(
    [property: JsonPropertyName("job_id")]         string         JobId,
    [property: JsonPropertyName("status")]         string         Status,
    [property: JsonPropertyName("created_at")]     DateTime       CreatedAt,
    [property: JsonPropertyName("started_at")]     DateTime?      StartedAt,
    [property: JsonPropertyName("completed_at")]   DateTime?      CompletedAt,
    [property: JsonPropertyName("pr_number")]      int?           PrNumber,
    [property: JsonPropertyName("analysis_result")] AnalysisResult? AnalysisResult,
    [property: JsonPropertyName("error_message")]  string?        ErrorMessage
);
