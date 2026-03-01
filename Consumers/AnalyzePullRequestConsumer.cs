using MassTransit;
using PullRequestAnalyzer.Controllers;
using PullRequestAnalyzer.Messages;
using PullRequestAnalyzer.Services;

namespace PullRequestAnalyzer.Consumers;

public sealed class AnalyzePullRequestConsumer : IConsumer<AnalyzePullRequestCommand>
{
    private readonly IAnalysisService  _analysis;
    private readonly RedisCacheService _cache;
    private readonly WebhookService    _webhook;
    private readonly IPublishEndpoint  _bus;
    private readonly ILogger<AnalyzePullRequestConsumer> _logger;

    public AnalyzePullRequestConsumer(
        IAnalysisService analysis,
        RedisCacheService cache,
        WebhookService webhook,
        IPublishEndpoint bus,
        ILogger<AnalyzePullRequestConsumer> logger)
    {
        _analysis = analysis;
        _cache    = cache;
        _webhook  = webhook;
        _bus      = bus;
        _logger   = logger;
    }

    public async Task Consume(ConsumeContext<AnalyzePullRequestCommand> context)
    {
        var cmd = context.Message;
        var pr  = cmd.PullRequestData;

        _logger.LogInformation("[Job {JobId}] Processing PR {Id}", cmd.JobId, pr.ToIdentifier());

        await UpdateJobAsync(cmd.JobId, "processing", pr.Number);

        try
        {
            var result = await _analysis.AnalyzeAsync(pr);

            await UpdateJobAsync(cmd.JobId, "completed", pr.Number, result: result);
            await _cache.SetAnalysisAsync(pr.Owner, pr.Repo, pr.Number, result);

            var ev = new PullRequestAnalyzedEvent(cmd.JobId, pr.Number, result);
            await _bus.Publish(ev);

            if (!string.IsNullOrEmpty(cmd.WebhookUrl))
                await _webhook.SendAsync(cmd.WebhookUrl, ev);

            _logger.LogInformation("[Job {JobId}] Completed", cmd.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Job {JobId}] Failed", cmd.JobId);

            await UpdateJobAsync(cmd.JobId, "failed", pr.Number, error: ex.Message);

            var ev = new PullRequestAnalysisFailedEvent(cmd.JobId, pr.Number, ex.Message);
            await _bus.Publish(ev);

            if (!string.IsNullOrEmpty(cmd.WebhookUrl))
                await _webhook.SendAsync(cmd.WebhookUrl, ev);

            throw;
        }
    }

    private async Task UpdateJobAsync(
        string jobId, string status, int prNumber,
        PullRequestAnalyzer.Models.AnalysisResult? result = null,
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
