namespace PullRequestAnalyzer.Models;

public record JobStatusRecord(
    string JobId,
    string Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int PrNumber,
    AnalysisResult? AnalysisResult,
    string? ErrorMessage
);