using System;
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
    public class AnalysisController : ControllerBase
    {
        private readonly LLMAnalysisService _analysisService;
        private readonly ILogger<AnalysisController> _logger;
        private readonly string _analysisResultsDirectory;

        public AnalysisController(LLMAnalysisService analysisService, ILogger<AnalysisController> logger, IConfiguration configuration)
        {
            _analysisService = analysisService;
            _logger = logger;
            _analysisResultsDirectory = configuration["AnalysisResultsDirectory"] ?? "./analysis_results";

            // Ensure analysis results directory exists
            if (!Directory.Exists(_analysisResultsDirectory))
            {
                Directory.CreateDirectory(_analysisResultsDirectory);
            }
        }

        /// <summary>
        /// POST /api/analyze
        /// Accepts PR JSON and returns structured analysis
        /// </summary>
        [HttpPost("analyze")]
        public async Task<ActionResult<AnalysisResult>> AnalyzePullRequest([FromBody] PullRequestData prData)
        {
            try
            {
                if (prData == null)
                {
                    return BadRequest(new { error = "PR data is required" });
                }

                _logger.LogInformation($"Starting analysis for PR #{prData.Number}");

                // Perform LLM analysis
                var analysisResult = await _analysisService.AnalyzePullRequestAsync(prData);

                // Cache the analysis result
                var resultFile = Path.Combine(_analysisResultsDirectory, $"analysis_{prData.Owner}_{prData.Repo}_{prData.Number}.json");
                var json = JsonConvert.SerializeObject(analysisResult, Formatting.Indented);
                System.IO.File.WriteAllText(resultFile, json);

                _logger.LogInformation($"Analysis saved to {resultFile}");

                return Ok(analysisResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing pull request");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// GET /api/analyze/:owner/:repo/:number
        /// Retrieves a cached analysis result
        /// </summary>
        [HttpGet("analyze/{owner}/{repo}/{number}")]
        public ActionResult<AnalysisResult> GetAnalysisResult(string owner, string repo, int number)
        {
            try
            {
                var resultFile = Path.Combine(_analysisResultsDirectory, $"analysis_{owner}_{repo}_{number}.json");

                if (!System.IO.File.Exists(resultFile))
                {
                    return NotFound(new { message = "Analysis result not found. Run analysis first." });
                }

                var json = System.IO.File.ReadAllText(resultFile);
                var analysisResult = JsonConvert.DeserializeObject<AnalysisResult>(json);

                return Ok(analysisResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving analysis for {owner}/{repo}#{number}");
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
