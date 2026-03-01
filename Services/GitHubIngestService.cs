using Octokit;
using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services;

public sealed class GitHubIngestService : IGitHubService
{
    private readonly GitHubClient _client;

    public GitHubIngestService(IConfiguration config)
    {
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                    ?? config["GitHub:Token"]
                    ?? throw new InvalidOperationException("GITHUB_TOKEN is not configured");

        _client = new GitHubClient(new ProductHeaderValue("PullRequestAnalyzer"))
        {
            Credentials = new Credentials(token)
        };
    }

    public async Task<PullRequestData> FetchPullRequestAsync(string owner, string repo, int prNumber)
    {
        var pr      = await _client.PullRequest.Get(owner, repo, prNumber);
        var commits = await _client.PullRequest.Commits(owner, repo, prNumber);
        var files   = await _client.PullRequest.Files(owner, repo, prNumber);

        return new PullRequestData
        {
            Id               = pr.Id,
            Number           = pr.Number,
            Title            = pr.Title,
            Description      = pr.Body ?? string.Empty,
            State            = pr.State.Value.ToString(),
            Author           = pr.User.Login,
            CreatedAt        = pr.CreatedAt.UtcDateTime,
            UpdatedAt        = pr.UpdatedAt.UtcDateTime,
            MergedAt         = pr.MergedAt?.UtcDateTime,
            Owner            = owner,
            Repo             = repo,
            Url              = pr.HtmlUrl,
            Additions        = pr.Additions,
            Deletions        = pr.Deletions,
            ChangedFilesCount = pr.ChangedFiles,
            Commits          = commits.Select(c => new CommitData
            {
                Sha         = c.Sha,
                Message     = c.Commit.Message,
                Author      = c.Commit.Author.Name,
                AuthorEmail = c.Commit.Author.Email,
                Timestamp   = c.Commit.Author.Date.UtcDateTime,
                Url         = c.HtmlUrl
            }).ToList(),
            ChangedFiles = files.Select(f => new ChangedFileData
            {
                Filename         = f.FileName,
                Status           = f.Status,
                Additions        = f.Additions,
                Deletions        = f.Deletions,
                Changes          = f.Changes,
                Patch            = f.Patch ?? string.Empty,
                PreviousFilename = f.PreviousFileName ?? string.Empty,
                BlobUrl          = f.BlobUrl,
                RawUrl           = f.RawUrl
            }).ToList()
        };
    }
}
