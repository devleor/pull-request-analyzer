using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Octokit;
using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services
{
    /// <summary>
    /// Service to ingest pull request data from GitHub API and normalize it.
    /// </summary>
    public class GitHubIngestService
    {
        private readonly GitHubClient _client;

        public GitHubIngestService(string githubToken)
        {
            var credentials = new Credentials(githubToken);
            _client = new GitHubClient(new ProductHeaderValue("PullRequestAnalyzer")) { Credentials = credentials };
        }

        /// <summary>
        /// Fetches a pull request and all its associated data from GitHub.
        /// </summary>
        public async Task<PullRequestData> FetchPullRequestAsync(string owner, string repo, int prNumber)
        {
            try
            {
                // Fetch PR metadata
                var pr = await _client.PullRequest.Get(owner, repo, prNumber);

                // Fetch commits
                var commits = await _client.PullRequest.Commits(owner, repo, prNumber);

                // Fetch changed files
                var files = await _client.PullRequest.Files(owner, repo, prNumber);

                // Normalize data
                var prData = new PullRequestData
                {
                    Id = pr.Id,
                    Number = pr.Number,
                    Title = pr.Title,
                    Description = pr.Body ?? string.Empty,
                    State = pr.State.Value.ToString(),
                    Author = pr.User.Login,
                    CreatedAt = pr.CreatedAt.UtcDateTime,
                    UpdatedAt = pr.UpdatedAt.UtcDateTime,
                    MergedAt = pr.MergedAt?.UtcDateTime,
                    Owner = owner,
                    Repo = repo,
                    Url = pr.HtmlUrl,
                    Additions = pr.Additions,
                    Deletions = pr.Deletions,
                    ChangedFilesCount = pr.ChangedFiles
                };

                // Add commits
                foreach (var commit in commits)
                {
                    prData.Commits.Add(new CommitData
                    {
                        Sha = commit.Sha,
                        Message = commit.Commit.Message,
                        Author = commit.Commit.Author.Name,
                        AuthorEmail = commit.Commit.Author.Email,
                        Timestamp = commit.Commit.Author.Date.UtcDateTime,
                        Url = commit.HtmlUrl
                    });
                }

                // Add changed files
                foreach (var file in files)
                {
                    prData.ChangedFiles.Add(new ChangedFileData
                    {
                        Filename = file.FileName,
                        Status = file.Status,
                        Additions = file.Additions,
                        Deletions = file.Deletions,
                        Changes = file.Changes,
                        Patch = file.Patch ?? string.Empty,
                        PreviousFilename = file.PreviousFileName,
                        BlobUrl = file.BlobUrl,
                        RawUrl = file.RawUrl
                    });
                }

                return prData;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error fetching PR {owner}/{repo}#{prNumber}: {ex.Message}", ex);
            }
        }
    }
}
