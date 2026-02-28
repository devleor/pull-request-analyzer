using System;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using PullRequestAnalyzer.Messages;
using PullRequestAnalyzer.Models;
using PullRequestAnalyzer.Services;

namespace PullRequestAnalyzer.Controllers
{
    [ApiController]
    [Route("api/v2")]
    public class AsyncAnalysisController : ControllerBase
    {
        private readonly IRequestClient<AnalyzePullRequestCommand> _analyzeClient;
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly JobStatusService _jobStatusService;
        private readonly ILogger<AsyncAnalysisController> _logger;

        public AsyncAnalysisController(
            IRequestClient<AnalyzePullRequestCommand> analyzeClient,
            IPublishEndpoint publishEndpoint,
            JobStatusService jobStatusService,
            ILogger<AsyncAnalysisController> logger)
        {
            _analyzeClient = analyzeClient;
            _publishEndpoint = publishEndpoint;
            _jobStatusService = jobStatusService;
            _logger = logger;
        }

        /// <summary>
        /// POST /api/v2/analyze-async
        /// Submits a PR for asynchronous analysis and returns a job ID.
        /// The client can then poll the job status or provide a webhook URL for notifications.
        /// </summary>
        [HttpPost("analyze-async")]
        public async Task<ActionResult<AnalysisJobResponse>> AnalyzeAsyncAsync(
            [FromBody] AnalyzeAsyncRequest request)
        {
            try
            {
                if (request?.PullRequestData == null)
                {
                    return BadRequest(new { error = "PR data is required" });
                }

                _logger.LogInformation($"Submitting async analysis for PR #{request.PullRequestData.Number}");

                // Create a job record
                var jobStatus = await _jobStatusService.CreateJobAsync(request.PullRequestData.Number);

                // Create the command
                var command = new AnalyzePullRequestCommand
                {
                    JobId = jobStatus.JobId,
                    PullRequestData = request.PullRequestData,
                    WebhookUrl = request.WebhookUrl
                };

                // Publish the command to the queue
                await _publishEndpoint.Publish(command);

                _logger.LogInformation($"Analysis job submitted: {jobStatus.JobId}");

                return Accepted(new AnalysisJobResponse
                {
                    JobId = jobStatus.JobId,
                    Status = "queued",
                    CreatedAt = jobStatus.CreatedAt,
                    StatusCheckUrl = $"/api/v2/jobs/{jobStatus.JobId}",
                    Message = "Your analysis has been queued. Use the StatusCheckUrl to check the status."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting async analysis");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// GET /api/v2/jobs/{jobId}
        /// Retrieves the status and result of an analysis job.
        /// </summary>
        [HttpGet("jobs/{jobId}")]
        public async Task<ActionResult<JobStatusResponse>> GetJobStatusAsync(string jobId)
        {
            try
            {
                var jobStatus = await _jobStatusService.GetJobStatusAsync(jobId);

                if (jobStatus == null)
                {
                    return NotFound(new { error = "Job not found" });
                }

                return Ok(new JobStatusResponse
                {
                    JobId = jobStatus.JobId,
                    Status = jobStatus.Status,
                    CreatedAt = jobStatus.CreatedAt,
                    StartedAt = jobStatus.StartedAt,
                    CompletedAt = jobStatus.CompletedAt,
                    PrNumber = jobStatus.PrNumber,
                    AnalysisResult = jobStatus.AnalysisResult,
                    ErrorMessage = jobStatus.ErrorMessage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving job status: {jobId}");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// GET /api/v2/jobs
        /// Lists all analysis jobs.
        /// </summary>
        [HttpGet("jobs")]
        public async Task<ActionResult<List<JobStatusResponse>>> ListJobsAsync()
        {
            try
            {
                var jobs = await _jobStatusService.GetAllJobsAsync();

                var responses = jobs.Select(j => new JobStatusResponse
                {
                    JobId = j.JobId,
                    Status = j.Status,
                    CreatedAt = j.CreatedAt,
                    StartedAt = j.StartedAt,
                    CompletedAt = j.CompletedAt,
                    PrNumber = j.PrNumber,
                    AnalysisResult = j.AnalysisResult,
                    ErrorMessage = j.ErrorMessage
                }).ToList();

                return Ok(responses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing jobs");
                return BadRequest(new { error = ex.Message });
            }
        }
    }

    /// <summary>
    /// Request model for async analysis.
    /// </summary>
    public class AnalyzeAsyncRequest
    {
        [JsonProperty("pull_request_data")]
        public PullRequestData? PullRequestData { get; set; }

        [JsonProperty("webhook_url")]
        public string? WebhookUrl { get; set; }
    }

    /// <summary>
    /// Response model for job submission.
    /// </summary>
    public class AnalysisJobResponse
    {
        [JsonProperty("job_id")]
        public string JobId { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("status_check_url")]
        public string StatusCheckUrl { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    /// <summary>
    /// Response model for job status.
    /// </summary>
    public class JobStatusResponse
    {
        [JsonProperty("job_id")]
        public string JobId { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("started_at")]
        public DateTime? StartedAt { get; set; }

        [JsonProperty("completed_at")]
        public DateTime? CompletedAt { get; set; }

        [JsonProperty("pr_number")]
        public int? PrNumber { get; set; }

        [JsonProperty("analysis_result")]
        public AnalysisResult? AnalysisResult { get; set; }

        [JsonProperty("error_message")]
        public string? ErrorMessage { get; set; }
    }
}
