using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services;

public interface IAnalysisService
{
    Task<AnalysisResult> AnalyzeAsync(PullRequestData pr);
}
