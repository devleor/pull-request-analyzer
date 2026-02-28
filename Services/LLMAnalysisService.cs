using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services
{
    /// <summary>
    /// Service to analyze PR data using LLM via OpenRouter with BYOK (Bring Your Own Key).
    /// </summary>
    public class LLMAnalysisService
    {
        private readonly HttpClient _httpClient;
        private readonly string? _openRouterApiKey;
        private readonly string? _openRouterModel;
        private readonly ILogger<LLMAnalysisService> _logger;
        private readonly DiffChunkingService _chunkingService;

        private const string OPENROUTER_API_URL = "https://openrouter.ai/api/v1/chat/completions";

        public LLMAnalysisService(ILogger<LLMAnalysisService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _chunkingService = new DiffChunkingService();
            
            // Get OpenRouter API key from environment or config
            _openRouterApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? 
                               configuration["OpenRouterApiKey"];
            
            // Get model selection from config (default to a capable model)
            _openRouterModel = configuration["OpenRouterModel"] ?? "meta-llama/llama-2-70b-chat";

            if (string.IsNullOrEmpty(_openRouterApiKey))
            {
                _logger.LogWarning("OpenRouter API key not configured. LLM analysis will not be available.");
            }
        }

        /// <summary>
        /// Analyzes a pull request using LLM.
        /// </summary>
        public async Task<AnalysisResult> AnalyzePullRequestAsync(PullRequestData prData)
        {
            if (string.IsNullOrEmpty(_openRouterApiKey))
            {
                throw new InvalidOperationException("OpenRouter API key not configured");
            }

            try
            {
                _logger.LogInformation($"Starting LLM analysis for PR #{prData.Number}");

                // Prepare the prompt
                var prompt = BuildAnalysisPrompt(prData);

                // Call OpenRouter API
                var analysisText = await CallOpenRouterAsync(prompt);

                // Parse the response
                var result = ParseAnalysisResponse(analysisText, prData);

                _logger.LogInformation($"Analysis completed for PR #{prData.Number}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error analyzing PR #{prData.Number}");
                throw;
            }
        }

        /// <summary>
        /// Builds the prompt for LLM analysis.
        /// </summary>
        private string BuildAnalysisPrompt(PullRequestData prData)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are an expert code reviewer analyzing a GitHub pull request.");
            sb.AppendLine("Provide a structured analysis of what was actually implemented based on the diffs.");
            sb.AppendLine();
            sb.AppendLine("=== PULL REQUEST INFORMATION ===");
            sb.AppendLine($"Title: {prData.Title}");
            sb.AppendLine($"Description: {prData.Description}");
            sb.AppendLine($"Author: {prData.Author}");
            sb.AppendLine($"State: {prData.State}");
            sb.AppendLine();

            sb.AppendLine("=== COMMITS ===");
            foreach (var commit in prData.Commits)
            {
                sb.AppendLine($"- {commit.Sha.Substring(0, 7)}: {commit.Message}");
            }
            sb.AppendLine();

            sb.AppendLine("=== CHANGED FILES ===");
            foreach (var file in prData.ChangedFiles)
            {
                sb.AppendLine($"File: {file.Filename}");
                sb.AppendLine($"  Status: {file.Status}");
                sb.AppendLine($"  Changes: +{file.Additions}/-{file.Deletions}");
                
                // Include a snippet of the diff (truncated for large diffs)
                if (!string.IsNullOrEmpty(file.Patch))
                {
                    var patchLines = file.Patch.Split('\n').Take(20).ToList();
                    sb.AppendLine("  Diff (first 20 lines):");
                    foreach (var line in patchLines)
                    {
                        sb.AppendLine($"    {line}");
                    }
                    if (file.Patch.Split('\n').Length > 20)
                    {
                        sb.AppendLine("    ... (truncated)");
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine("=== ANALYSIS TASK ===");
            sb.AppendLine("Provide a JSON response with the following structure:");
            sb.AppendLine("{");
            sb.AppendLine("  \"executive_summary\": [\"bullet 1\", \"bullet 2\", ...],");
            sb.AppendLine("  \"change_units\": [");
            sb.AppendLine("    {");
            sb.AppendLine("      \"type\": \"feature|bugfix|refactor|test|documentation|performance\",");
            sb.AppendLine("      \"title\": \"Short title\",");
            sb.AppendLine("      \"description\": \"Detailed description\",");
            sb.AppendLine("      \"affected_files\": [\"file1.py\", \"file2.py\"],");
            sb.AppendLine("      \"inferred_intent\": \"Why this change was made\",");
            sb.AppendLine("      \"confidence_level\": \"high|medium|low\",");
            sb.AppendLine("      \"test_coverage_signal\": \"high|medium|low|none\"");
            sb.AppendLine("    }");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"risks_and_concerns\": [\"risk 1\", \"risk 2\", ...],");
            sb.AppendLine("  \"claimed_vs_actual\": {");
            sb.AppendLine("    \"alignment_assessment\": \"aligned|partially_aligned|misaligned\",");
            sb.AppendLine("    \"discrepancies\": [\"discrepancy 1\", ...]");
            sb.AppendLine("  }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// Calls OpenRouter API with the prompt.
        /// </summary>
        private async Task<string> CallOpenRouterAsync(string prompt)
        {
            var requestBody = new
            {
                model = _openRouterModel,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.7,
                max_tokens = 4000
            };

            var content = new StringContent(
                JsonConvert.SerializeObject(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            // Add OpenRouter headers
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_openRouterApiKey}");
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/yourusername/PullRequestAnalyzer");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "PullRequestAnalyzer");

            try
            {
                var response = await _httpClient.PostAsync(OPENROUTER_API_URL, content);
                response.EnsureSuccessStatusCode();

                var responseText = await response.Content.ReadAsStringAsync();
                var responseJson = JObject.Parse(responseText);

                var analysisText = responseJson["choices"]?[0]?["message"]?["content"]?.ToString();
                if (string.IsNullOrEmpty(analysisText))
                {
                    throw new InvalidOperationException("Empty response from OpenRouter API");
                }

                return analysisText;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error calling OpenRouter API");
                throw new InvalidOperationException($"OpenRouter API error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Parses the LLM response into an AnalysisResult.
        /// </summary>
        private AnalysisResult ParseAnalysisResponse(string responseText, PullRequestData prData)
        {
            try
            {
                // Extract JSON from the response (it might be wrapped in markdown code blocks)
                var jsonText = ExtractJsonFromResponse(responseText);
                var analysisJson = JObject.Parse(jsonText);

                var result = new AnalysisResult
                {
                    PrNumber = prData.Number,
                    PrTitle = prData.Title,
                    AnalysisTimestamp = DateTime.UtcNow,
                    ConfidenceScore = 0.85 // Default confidence
                };

                // Parse executive summary
                if (analysisJson["executive_summary"] is JArray summaryArray)
                {
                    result.ExecutiveSummary = summaryArray.Select(s => s.ToString()).ToList();
                }

                // Parse change units
                if (analysisJson["change_units"] is JArray unitsArray)
                {
                    foreach (var unitJson in unitsArray)
                    {
                        var unit = new ChangeUnit
                        {
                            Id = Guid.NewGuid().ToString().Substring(0, 8),
                            Type = unitJson["type"]?.ToString() ?? "unknown",
                            Title = unitJson["title"]?.ToString() ?? "",
                            Description = unitJson["description"]?.ToString() ?? "",
                            InferredIntent = unitJson["inferred_intent"]?.ToString() ?? "",
                            ConfidenceLevel = unitJson["confidence_level"]?.ToString() ?? "medium",
                            TestCoverageSignal = unitJson["test_coverage_signal"]?.ToString() ?? "none"
                        };

                        if (unitJson["affected_files"] is JArray filesArray)
                        {
                            unit.AffectedFiles = filesArray.Select(f => f.ToString()).ToList();
                        }

                        result.ChangeUnits.Add(unit);
                    }
                }

                // Parse risks and concerns
                if (analysisJson["risks_and_concerns"] is JArray risksArray)
                {
                    result.RisksAndConcerns = risksArray.Select(r => r.ToString()).ToList();
                }

                // Parse claimed vs actual
                if (analysisJson["claimed_vs_actual"] is JObject claimedVsActual)
                {
                    result.ClaimedVsActual.AlignmentAssessment = claimedVsActual["alignment_assessment"]?.ToString() ?? "unknown";
                    if (claimedVsActual["discrepancies"] is JArray discrepancies)
                    {
                        result.ClaimedVsActual.Discrepancies = discrepancies.Select(d => d.ToString()).ToList();
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing LLM analysis response");
                throw new InvalidOperationException($"Failed to parse analysis response: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Extracts JSON from response text (handles markdown code blocks).
        /// </summary>
        private string ExtractJsonFromResponse(string responseText)
        {
            // Try to find JSON in markdown code blocks first
            var jsonMatch = System.Text.RegularExpressions.Regex.Match(
                responseText,
                @"```(?:json)?\s*([\s\S]*?)```"
            );

            if (jsonMatch.Success)
            {
                return jsonMatch.Groups[1].Value.Trim();
            }

            // If no code block, try to find raw JSON
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                return responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }

            throw new InvalidOperationException("Could not extract JSON from LLM response");
        }
    }
}
