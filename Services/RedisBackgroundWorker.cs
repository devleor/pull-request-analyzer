using PullRequestAnalyzer.Controllers;
using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services;

public sealed class RedisBackgroundWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly RedisJobQueue   _queue;
    private readonly RedLockService  _lock;
    private readonly IAnalysisService _analysis;
    private readonly RedisCacheService _cache;
    private readonly WebhookService  _webhook;
    private readonly ILogger<RedisBackgroundWorker> _logger;

    public RedisBackgroundWorker(
        RedisJobQueue queue,
        RedLockService @lock,
        IAnalysisService analysis,
        RedisCacheService cache,
        WebhookService webhook,
        ILogger<RedisBackgroundWorker> logger)
    {
        _queue    = queue;
        _lock     = @lock;
        _analysis = analysis;
        _cache    = cache;
        _webhook  = webhook;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Redis background worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var commands = await _queue.DequeueAsync(stoppingToken);

                foreach (var cmd in commands)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    await ProcessAsync(cmd);
                }

                if (!commands.Any())
                    await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in worker poll loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("Redis background worker stopped");
    }

    private async Task ProcessAsync(Messages.AnalyzePullRequestCommand cmd)
    {
        var pr    = cmd.PullRequestData;
        var msgId = cmd.StreamMessageId ?? string.Empty;

        _logger.LogInformation("[Job {JobId}] Processing PR {Id}", cmd.JobId, pr.ToIdentifier());

        await SetJobStatusAsync(cmd.JobId, "processing", pr.Number);

        var processed = await _lock.ExecuteWithLockAsync(pr.Owner, pr.Repo, pr.Number, async () =>
        {
            try
            {
                var cached = await _cache.GetAnalysisAsync<AnalysisResult>(pr.Owner, pr.Repo, pr.Number);

                if (cached is not null)
                {
                    await SetJobStatusAsync(cmd.JobId, "completed", pr.Number, result: cached);
                    await _queue.AcknowledgeAsync(msgId);
                    return;
                }

                var result = await _analysis.AnalyzeAsync(pr);

                await _cache.SetAnalysisAsync(pr.Owner, pr.Repo, pr.Number, result);
                await SetJobStatusAsync(cmd.JobId, "completed", pr.Number, result: result);

                if (!string.IsNullOrEmpty(cmd.WebhookUrl))
                    await _webhook.SendAsync(cmd.WebhookUrl, result);

                await _queue.AcknowledgeAsync(msgId);

                _logger.LogInformation("[Job {JobId}] Completed", cmd.JobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job {JobId}] Failed", cmd.JobId);
                await SetJobStatusAsync(cmd.JobId, "failed", pr.Number, error: ex.Message);
                await _queue.MoveToDeadLetterAsync(msgId, ex.Message);

                if (!string.IsNullOrEmpty(cmd.WebhookUrl))
                    await _webhook.SendAsync(cmd.WebhookUrl, new { job_id = cmd.JobId, status = "failed", error = ex.Message });
            }
        });

        if (!processed)
            _logger.LogWarning("[Job {JobId}] Lock not acquired for PR {Id} — will retry", cmd.JobId, pr.ToIdentifier());
    }

    private async Task SetJobStatusAsync(
        string jobId, string status, int prNumber,
        AnalysisResult? result = null, string? error = null)
    {
        var existing = await _cache.GetJobAsync<JobStatusRecord>(jobId);

        var updated = existing is null
            ? new JobStatusRecord(jobId, status, DateTime.UtcNow, null, null, prNumber, result, error)
            : existing with
            {
                Status         = status,
                StartedAt      = status == "processing" ? (existing.StartedAt ?? DateTime.UtcNow) : existing.StartedAt,
                CompletedAt    = status is "completed" or "failed" ? DateTime.UtcNow : existing.CompletedAt,
                AnalysisResult = result ?? existing.AnalysisResult,
                ErrorMessage   = error  ?? existing.ErrorMessage
            };

        await _cache.SetJobAsync(jobId, updated);
    }
}
