using Microsoft.Extensions.DependencyInjection;
using PullRequestAnalyzer.Controllers;
using PullRequestAnalyzer.Messages;
using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services;

public sealed class AnalysisBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval  = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ErrorCooldown = TimeSpan.FromSeconds(5);

    private readonly JobQueueService    _queue;
    private readonly DistributedLockService   _lock;
    private readonly RedisCacheService _cache;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AnalysisBackgroundService> _logger;

    public AnalysisBackgroundService(
        JobQueueService queue,
        DistributedLockService @lock,
        RedisCacheService cache,
        IServiceProvider serviceProvider,
        ILogger<AnalysisBackgroundService> logger)
    {
        _queue    = queue;
        _lock     = @lock;
        _cache    = cache;
        _serviceProvider = serviceProvider;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var commands = (await _queue.DequeueAsync(stoppingToken)).ToList();

                foreach (var cmd in commands)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    await ProcessAsync(cmd, stoppingToken);
                }

                if (commands.Count == 0)
                    await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in worker poll loop");
                await Task.Delay(ErrorCooldown, stoppingToken);
            }
        }

        _logger.LogInformation("Worker stopped");
    }

    private async Task ProcessAsync(AnalyzePullRequestCommand cmd, CancellationToken ct)
    {
        var pr    = cmd.PullRequestData;
        var msgId = cmd.StreamMessageId ?? string.Empty;

        _logger.LogInformation("[Job {JobId}] Starting PR {Id}", cmd.JobId, pr.ToIdentifier());

        await SetStatusAsync(cmd.JobId, "processing", pr.Number);

        var processed = await _lock.ExecuteWithLockAsync(pr.Owner, pr.Repo, pr.Number, async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var analysis = scope.ServiceProvider.GetRequiredService<IAnalysisService>();
            var webhook = scope.ServiceProvider.GetRequiredService<WebhookService>();

            try
            {
                var result = await analysis.AnalyzeAsync(pr);

                await SetStatusAsync(cmd.JobId, "completed", pr.Number, result: result);
                await _queue.AcknowledgeAsync(msgId);

                if (!string.IsNullOrEmpty(cmd.WebhookUrl))
                    await webhook.SendAsync(cmd.WebhookUrl, new PullRequestAnalyzedEvent(
                        cmd.JobId, pr.Number, result, DateTime.UtcNow));

                _logger.LogInformation("[Job {JobId}] Completed", cmd.JobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job {JobId}] Failed", cmd.JobId);

                await SetStatusAsync(cmd.JobId, "failed", pr.Number, error: ex.Message);
                await _queue.MoveToDeadLetterAsync(msgId, ex.Message);

                if (!string.IsNullOrEmpty(cmd.WebhookUrl))
                    await webhook.SendAsync(cmd.WebhookUrl, new PullRequestAnalysisFailedEvent(
                        cmd.JobId, pr.Number, ex.Message, DateTime.UtcNow));
            }
        });

        if (!processed)
            _logger.LogWarning("[Job {JobId}] Lock not acquired for {Id} — will retry", cmd.JobId, pr.ToIdentifier());
    }

    private async Task SetStatusAsync(
        string jobId, string status, int prNumber,
        AnalysisResult? result = null,
        string? error = null)
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
