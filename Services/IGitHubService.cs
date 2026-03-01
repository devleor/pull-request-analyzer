using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services;

public interface IGitHubService
{
    Task<PullRequestData> FetchPullRequestAsync(string owner, string repo, int prNumber);
}
