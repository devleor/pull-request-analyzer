using Newtonsoft.Json;
using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services
{
    /// <summary>
    /// Service to manage and persist job status for async analysis operations.
    /// Uses file-based storage for simplicity in this prototype.
    /// </summary>
    public class JobStatusService
    {
        private readonly string _jobStatusDirectory;
        private readonly ILogger<JobStatusService> _logger;

        public JobStatusService(ILogger<JobStatusService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _jobStatusDirectory = configuration["JobStatusDirectory"] ?? "./job_status";

            if (!Directory.Exists(_jobStatusDirectory))
            {
                Directory.CreateDirectory(_jobStatusDirectory);
            }
        }

        public class JobStatus
        {
            [JsonProperty("job_id")]
            public string JobId { get; set; }

            [JsonProperty("status")]
            public string Status { get; set; } // "queued", "processing", "completed", "failed"

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

        public async Task<JobStatus> CreateJobAsync(int prNumber)
        {
            var jobStatus = new JobStatus
            {
                JobId = Guid.NewGuid().ToString(),
                Status = "queued",
                CreatedAt = DateTime.UtcNow,
                PrNumber = prNumber
            };

            await SaveJobStatusAsync(jobStatus);
            _logger.LogInformation($"Job created: {jobStatus.JobId} for PR #{prNumber}");

            return jobStatus;
        }

        public async Task UpdateJobStatusAsync(string jobId, string status, AnalysisResult? analysisResult = null, string? errorMessage = null)
        {
            var jobStatus = await GetJobStatusAsync(jobId);

            if (jobStatus == null)
            {
                _logger.LogWarning($"Job not found: {jobId}");
                return;
            }

            jobStatus.Status = status;

            if (status == "processing" && jobStatus.StartedAt == null)
            {
                jobStatus.StartedAt = DateTime.UtcNow;
            }

            if (status == "completed" || status == "failed")
            {
                jobStatus.CompletedAt = DateTime.UtcNow;
            }

            if (analysisResult != null)
            {
                jobStatus.AnalysisResult = analysisResult;
            }

            if (errorMessage != null)
            {
                jobStatus.ErrorMessage = errorMessage;
            }

            await SaveJobStatusAsync(jobStatus);
            _logger.LogInformation($"Job updated: {jobId} -> {status}");
        }

        public async Task<JobStatus?> GetJobStatusAsync(string jobId)
        {
            var filePath = Path.Combine(_jobStatusDirectory, $"{jobId}.json");

            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var jobStatus = JsonConvert.DeserializeObject<JobStatus>(json);
                return jobStatus;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error reading job status: {jobId}");
                return null;
            }
        }

        public async Task<List<JobStatus>> GetAllJobsAsync()
        {
            var jobs = new List<JobStatus>();

            try
            {
                var files = Directory.GetFiles(_jobStatusDirectory, "*.json");

                foreach (var file in files)
                {
                    var json = await File.ReadAllTextAsync(file);
                    var jobStatus = JsonConvert.DeserializeObject<JobStatus>(json);
                    if (jobStatus != null)
                    {
                        jobs.Add(jobStatus);
                    }
                }

                return jobs.OrderByDescending(j => j.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading all job statuses");
                return jobs;
            }
        }

        private async Task SaveJobStatusAsync(JobStatus jobStatus)
        {
            try
            {
                var filePath = Path.Combine(_jobStatusDirectory, $"{jobStatus.JobId}.json");
                var json = JsonConvert.SerializeObject(jobStatus, Formatting.Indented);
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving job status: {jobStatus.JobId}");
            }
        }
    }
}
