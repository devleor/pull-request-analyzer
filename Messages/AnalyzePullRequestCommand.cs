using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Messages
{
    /// <summary>
    /// Command to analyze a pull request asynchronously via MassTransit.
    /// </summary>
    public class AnalyzePullRequestCommand
    {
        public string JobId { get; set; } = Guid.NewGuid().ToString();
        public PullRequestData PullRequestData { get; set; }
        public string? WebhookUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Event published when analysis is completed successfully.
    /// </summary>
    public class PullRequestAnalyzedEvent
    {
        public string JobId { get; set; }
        public int PrNumber { get; set; }
        public AnalysisResult AnalysisResult { get; set; }
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Event published when analysis fails.
    /// </summary>
    public class PullRequestAnalysisFailedEvent
    {
        public string JobId { get; set; }
        public int PrNumber { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime FailedAt { get; set; } = DateTime.UtcNow;
    }
}
