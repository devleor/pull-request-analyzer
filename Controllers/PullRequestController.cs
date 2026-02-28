using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using PullRequestAnalyzer.Models;
using PullRequestAnalyzer.Services;

namespace PullRequestAnalyzer.Controllers
{
    [ApiController]
    [Route("api")]
    public class PullRequestController : ControllerBase
    {
        private readonly GitHubIngestService? _githubService;
        private readonly ILogger<PullRequestController> _logger;
        private readonly string _prDataDirectory;

        public PullRequestController(ILogger<PullRequestController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _prDataDirectory = configuration["PRDataDirectory"] ?? "./pr_data";
            
            // Initialize GitHub service with token from environment or config
            var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? 
                             configuration["GitHubToken"];
            if (!string.IsNullOrEmpty(githubToken))
            {
                _githubService = new GitHubIngestService(githubToken);
            }

            // Ensure PR data directory exists
            if (!Directory.Exists(_prDataDirectory))
            {
                Directory.CreateDirectory(_prDataDirectory);
            }
        }

        /// <summary>
        /// GET /api/pull-requests/:owner/:repo/:number
        /// Returns normalized PR JSON
        /// </summary>
        [HttpGet("pull-requests/{owner}/{repo}/{number}")]
        public async Task<ActionResult<PullRequestData>> GetPullRequest(string owner, string repo, int number)
        {
            try
            {
                _logger.LogInformation($"Fetching PR {owner}/{repo}#{number}");

                // First, try to load from local cache
                var cachedFile = Path.Combine(_prDataDirectory, $"{owner}_{repo}_{number}.json");
                if (System.IO.File.Exists(cachedFile))
                {
                    _logger.LogInformation($"Loading PR from cache: {cachedFile}");
                    var json = System.IO.File.ReadAllText(cachedFile);
                    var prData = JsonConvert.DeserializeObject<PullRequestData>(json);
                    return Ok(prData);
                }

                // If not cached and GitHub service is available, fetch from GitHub
                if (_githubService != null)
                {
                    _logger.LogInformation($"Fetching PR from GitHub API");
                    var prData = await _githubService.FetchPullRequestAsync(owner, repo, number);
                    
                    // Cache the result
                    var json = JsonConvert.SerializeObject(prData, Formatting.Indented);
                    System.IO.File.WriteAllText(cachedFile, json);
                    
                    return Ok(prData);
                }

                return NotFound(new { message = "PR not found in cache and GitHub token not configured" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching PR {owner}/{repo}#{number}");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// GET /api/pull-requests/:owner/:repo/:number/commits
        /// Returns commit list for a PR
        /// </summary>
        [HttpGet("pull-requests/{owner}/{repo}/{number}/commits")]
        public async Task<ActionResult<List<CommitData>>> GetPullRequestCommits(string owner, string repo, int number)
        {
            try
            {
                _logger.LogInformation($"Fetching commits for PR {owner}/{repo}#{number}");

                var prResponse = await GetPullRequest(owner, repo, number);
                if (prResponse.Result is OkObjectResult okResult && okResult.Value is PullRequestData prData)
                {
                    return Ok(prData.Commits);
                }

                return NotFound(new { message = "PR not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching commits for PR {owner}/{repo}#{number}");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// POST /api/pull-requests/:owner/:repo/:number/upload
        /// Upload a PR JSON file directly
        /// </summary>
        [HttpPost("pull-requests/{owner}/{repo}/{number}/upload")]
        public async Task<ActionResult> UploadPullRequestData(string owner, string repo, int number, IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { error = "No file provided" });
                }

                using (var reader = new StreamReader(file.OpenReadStream()))
                {
                    var json = await reader.ReadToEndAsync();
                    var prData = JsonConvert.DeserializeObject<PullRequestData>(json);

                    if (prData == null)
                    {
                        return BadRequest(new { error = "Invalid PR JSON format" });
                    }

                    // Save to cache
                    var cachedFile = Path.Combine(_prDataDirectory, $"{owner}_{repo}_{number}.json");
                    System.IO.File.WriteAllText(cachedFile, json);

                    _logger.LogInformation($"PR data uploaded and cached: {cachedFile}");
                    return Ok(new { message = "PR data uploaded successfully", path = cachedFile });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error uploading PR data for {owner}/{repo}#{number}");
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
