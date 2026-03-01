using Microsoft.AspNetCore.Mvc;
using PullRequestAnalyzer.Models;
using PullRequestAnalyzer.Services;

namespace PullRequestAnalyzer.Controllers;

[ApiController]
[Route("api/pull-requests")]
public sealed class PullRequestController : ControllerBase
{
    private readonly IGitHubService    _github;
    private readonly RedisCacheService _cache;
    private readonly ILogger<PullRequestController> _logger;

    public PullRequestController(
        IGitHubService github,
        RedisCacheService cache,
        ILogger<PullRequestController> logger)
    {
        _github = github;
        _cache  = cache;
        _logger = logger;
    }

    [HttpGet("{owner}/{repo}/{number:int}")]
    public async Task<ActionResult<PullRequestData>> GetPullRequest(
        string owner, string repo, int number)
    {
        var cached = await _cache.GetPrAsync<PullRequestData>(owner, repo, number);
        if (cached is not null)
            return Ok(cached);

        _logger.LogInformation("Fetching PR {Owner}/{Repo}#{Number} from GitHub", owner, repo, number);

        var pr = await _github.FetchPullRequestAsync(owner, repo, number);
        await _cache.SetPrAsync(owner, repo, number, pr);

        return Ok(pr);
    }

    [HttpGet("{owner}/{repo}/{number:int}/commits")]
    public async Task<ActionResult<List<CommitData>>> GetCommits(
        string owner, string repo, int number)
    {
        var result = await GetPullRequest(owner, repo, number);

        return result.Result is OkObjectResult { Value: PullRequestData pr }
            ? Ok(pr.Commits)
            : NotFound(new { error = "PR not found" });
    }

    [HttpPost("{owner}/{repo}/{number:int}/upload")]
    public async Task<ActionResult> Upload(
        string owner, string repo, int number, IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file provided" });

        using var reader = new StreamReader(file.OpenReadStream());
        var json = await reader.ReadToEndAsync();

        var pr = System.Text.Json.JsonSerializer.Deserialize<PullRequestData>(json);
        if (pr is null)
            return BadRequest(new { error = "Invalid PR JSON format" });

        await _cache.SetPrAsync(owner, repo, number, pr);

        return Ok(new { message = "PR data uploaded successfully" });
    }
}
