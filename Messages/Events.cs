using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Messages;

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
