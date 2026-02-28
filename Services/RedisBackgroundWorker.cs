using PullRequestAnalyzer.Messages;

namespace PullRequestAnalyzer.Services
{
    /// <summary>
    /// Background worker that consumes the Redis Streams queue and processes
    /// PR analysis jobs with RedLock to prevent duplicate processing.
    ///
    /// Flow:
    ///   1. Poll Redis Stream for new commands
    ///   2. Acquire RedLock for the PR (skip if already locked)
    ///   3. Process analysis via LLMAnalysisService
    ///   4. Update job status in Redis
    ///   5. Send webhook notification
    ///   6. Acknowledge message in stream
    ///   7. Release lock
    /// </summary>
    public class RedisBackgroundWorker : BackgroundService
    {
        private readonly RedisJobQueue _queue;
        private readonly RedLockService _redLock;
        private readonly LLMAnalysisService _llmService;
        private readonly RedisCacheService _cache;
        private readonly WebhookService _webhook;
        private readonly ILogger<RedisBackgroundWorker> _logger;

        private static readonly TimeSpan POLL_INTERVAL = TimeSpan.FromSeconds(2);

        public RedisBackgroundWorker(
            RedisJobQueue queue,
            RedLockService redLock,
            LLMAnalysisService llmService,
            RedisCacheService cache,
            WebhookService webhook,
            ILogger<RedisBackgroundWorker> logger)
        {
            _queue      = queue;
            _redLock    = redLock;
            _llmService = llmService;
            _cache      = cache;
            _webhook    = webhook;
            _logger     = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Redis Background Worker started. Polling stream every {Interval}s", POLL_INTERVAL.TotalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var commands = await _queue.DequeueAsync(stoppingToken);

                    foreach (var command in commands)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        await ProcessCommandAsync(command, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in Redis Background Worker poll loop");
                }

                await Task.Delay(POLL_INTERVAL, stoppingToken);
            }

            _logger.LogInformation("Redis Background Worker stopped");
        }

        private async Task ProcessCommandAsync(AnalyzePullRequestCommand command, CancellationToken ct)
        {
            var pr     = command.PullRequestData;
            var jobId  = command.JobId;
            var msgId  = command.StreamMessageId ?? "";

            if (pr == null)
            {
                _logger.LogWarning("Received command with null PR data. JobId: {JobId}", jobId);
                await _queue.AcknowledgeAsync(msgId);
                return;
            }

            // PullRequestData uses .Repo not .Repository
            var owner = pr.Owner ?? "unknown";
            var repo  = pr.Repo  ?? "unknown";

            _logger.LogInformation(
                "Processing job {JobId} for PR {Owner}/{Repo}#{Number}",
                jobId, owner, repo, pr.Number);

            // Update job status → processing
            await UpdateJobStatusAsync(jobId, "processing", null, null);

            // Acquire RedLock to prevent duplicate processing
            var acquired = await _redLock.ExecuteWithLockAsync(
                owner, repo, pr.Number,
                async () =>
                {
                    try
                    {
                        // Check if analysis is already cached
                        var cached = await _cache.GetAnalysisAsync<object>(owner, repo, pr.Number);

                        if (cached != null)
                        {
                            _logger.LogInformation("Analysis cache HIT for PR#{Number} — skipping LLM call", pr.Number);
                            await UpdateJobStatusAsync(jobId, "completed", cached, null);
                            await _queue.AcknowledgeAsync(msgId);
                            return;
                        }

                        // Run LLM analysis
                        var result = await _llmService.AnalyzePullRequestAsync(pr);

                        // Cache the result
                        await _cache.SetAnalysisAsync(owner, repo, pr.Number, result);

                        // Update job status → completed
                        await UpdateJobStatusAsync(jobId, "completed", result, null);

                        // Send webhook if provided
                        if (!string.IsNullOrEmpty(command.WebhookUrl))
                        {
                            await _webhook.SendWebhookAsync(command.WebhookUrl, new
                            {
                                job_id          = jobId,
                                pr_number       = pr.Number,
                                status          = "completed",
                                analysis_result = result,
                                completed_at    = DateTime.UtcNow
                            });
                        }

                        // Acknowledge stream message
                        await _queue.AcknowledgeAsync(msgId);

                        _logger.LogInformation("Job {JobId} completed successfully", jobId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing job {JobId}", jobId);

                        await UpdateJobStatusAsync(jobId, "failed", null, ex.Message);

                        // Move to dead-letter queue after failure
                        await _queue.MoveToDeadLetterAsync(msgId, ex.Message);

                        // Notify webhook of failure
                        if (!string.IsNullOrEmpty(command.WebhookUrl))
                        {
                            await _webhook.SendWebhookAsync(command.WebhookUrl, new
                            {
                                job_id     = jobId,
                                pr_number  = pr.Number,
                                status     = "failed",
                                error      = ex.Message,
                                failed_at  = DateTime.UtcNow
                            });
                        }
                    }
                });

            if (!acquired)
            {
                _logger.LogWarning(
                    "Could not acquire lock for PR#{Number} — re-queuing job {JobId}",
                    pr.Number, jobId);
                // Do not acknowledge — message stays pending and will be retried
            }
        }

        private async Task UpdateJobStatusAsync(string jobId, string status, object? result, string? error)
        {
            var jobData = new
            {
                job_id     = jobId,
                status,
                result,
                error,
                updated_at = DateTime.UtcNow
            };

            await _cache.SetJobAsync(jobId, jobData);
        }
    }
}
