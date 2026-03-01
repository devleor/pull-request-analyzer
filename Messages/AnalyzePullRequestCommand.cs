using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Messages;

public sealed class AnalyzePullRequestCommand
{
    public string          JobId           { get; init; } = Guid.NewGuid().ToString();
    public PullRequestData PullRequestData { get; init; } = null!;
    public string?         WebhookUrl      { get; init; }
    public DateTime        CreatedAt       { get; init; } = DateTime.UtcNow;
    public string?         StreamMessageId { get; set; }
}

public sealed record PullRequestAnalyzedEvent(
    string         JobId,
    int            PrNumber,
    AnalysisResult AnalysisResult,
    DateTime       CompletedAt);

public sealed record PullRequestAnalysisFailedEvent(
    string   JobId,
    int      PrNumber,
    string   ErrorMessage,
    DateTime FailedAt);
