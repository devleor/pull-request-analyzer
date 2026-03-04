#pragma warning disable SKEXP0010 // Suppress experimental API warnings

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services;

/// <summary>
/// Production-ready Semantic Kernel Analysis Service with OpenTelemetry tracing to Langfuse
/// </summary>
public sealed class LlmAnalysisService : IAnalysisService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly DiffChunkingService _chunker;
    private readonly IPromptTemplateService _promptTemplates;
    private readonly ILogger<LlmAnalysisService> _logger;
    private readonly string _modelName;

    public LlmAnalysisService(
        DiffChunkingService chunker,
        IPromptTemplateService promptTemplates,
        IConfiguration config,
        ILogger<LlmAnalysisService> logger)
    {
        _chunker = chunker;
        _promptTemplates = promptTemplates;
        _logger = logger;

        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
                     ?? config["OpenRouter:ApiKey"]
                     ?? throw new InvalidOperationException("OPENROUTER_API_KEY is not configured");

        _modelName = Environment.GetEnvironmentVariable("OPENROUTER_MODEL")
                    ?? config["OpenRouter:Model"]
                    ?? "liquid/lfm-2.5-1.2b-instruct:free";

        // Build Semantic Kernel with OpenRouter
        var builder = Kernel.CreateBuilder();

        // Configure for OpenRouter (OpenAI-compatible)
        builder.AddOpenAIChatCompletion(
            modelId: _modelName,
            apiKey: apiKey,
            endpoint: new Uri("https://openrouter.ai/api/v1"),
            httpClient: new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(120),
                DefaultRequestHeaders =
                {
                    { "HTTP-Referer", "https://github.com/devleor/pull-request-analyzer" },
                    { "X-Title", "PullRequestAnalyzer" }
                }
            });

        _kernel = builder.Build();
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();

        _logger.LogInformation("OpenTelemetry-based Semantic Kernel service initialized with model: {Model}", _modelName);
    }

    public async Task<AnalysisResult> AnalyzeAsync(PullRequestData pr)
    {
        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString();

        // Start OpenTelemetry activity (span)
        using var activity = TelemetryService.StartActivity(
            $"pr_analysis.{pr.Number}",
            ActivityKind.Internal,
            new Dictionary<string, object?>
            {
                ["pr.owner"] = pr.Owner,
                ["pr.repo"] = pr.Repo,
                ["pr.number"] = pr.Number,
                ["pr.title"] = pr.Title,
                ["pr.author"] = pr.Author,
                ["pr.file_count"] = pr.ChangedFiles.Count,
                ["pr.commit_count"] = pr.Commits.Count,
                ["correlation_id"] = correlationId,
                ["llm.model"] = _modelName
            });

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["PR"] = $"{pr.Owner}/{pr.Repo}#{pr.Number}",
            ["Operation"] = "PRAnalysis"
        });

        _logger.LogInformation("Starting PR analysis - Files: {FileCount}, Commits: {CommitCount}",
            pr.ChangedFiles.Count, pr.Commits.Count);

        try
        {
            // Load prompts from Redis
            TelemetryService.AddEvent("load_prompts.start");

            var systemPrompt = await _promptTemplates.GetPromptTemplateAsync("pr_analysis_system")
                              ?? throw new InvalidOperationException("System prompt 'pr_analysis_system' not found in Redis. Please initialize prompts.");

            var fewShotPrompt = await _promptTemplates.GetPromptTemplateAsync("pr_analysis_fewshot")
                               ?? GetDefaultFewShotExample();

            TelemetryService.AddEvent("load_prompts.complete", new Dictionary<string, object?>
            {
                ["system_prompt_length"] = systemPrompt.Length,
                ["fewshot_prompt_length"] = fewShotPrompt.Length
            });

            // Build the PR data content
            var prDataContent = BuildPRDataContent(pr);

            // Create chat history
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(systemPrompt);
            chatHistory.AddUserMessage(fewShotPrompt);
            chatHistory.AddAssistantMessage("Understood. I will follow this format and ground my analysis in the actual diffs.");
            chatHistory.AddUserMessage(prDataContent);

            // Calculate prompt size
            var totalPromptLength = systemPrompt.Length + fewShotPrompt.Length + prDataContent.Length;

            activity?.SetTag("llm.prompt_tokens_estimate", totalPromptLength / 4);
            activity?.SetTag("llm.max_tokens", 4096);
            activity?.SetTag("llm.temperature", 0.2);

            _logger.LogDebug("Conversation constructed - System: {SystemLength} chars, PR Data: {DataLength} chars",
                systemPrompt.Length, prDataContent.Length);

            // Configure execution settings
            var executionSettings = new OpenAIPromptExecutionSettings
            {
                Temperature = 0.2,
                MaxTokens = 4096
                // ResponseFormat = "json_object" // Not supported by all models
            };

            // Create a specific span for LLM call with Langfuse-compatible format
            using var llmSpan = TelemetryService.StartActivity(
                "generation",  // Langfuse expects "generation" as the span name
                ActivityKind.Client,
                new Dictionary<string, object?>
                {
                    ["gen_ai.system"] = "openrouter",
                    ["gen_ai.request.model"] = _modelName,
                    ["gen_ai.request.temperature"] = 0.2,
                    ["gen_ai.request.max_tokens"] = 4096,
                    ["gen_ai.request.top_p"] = 1.0
                });

            // Build messages array for Langfuse
            var messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = fewShotPrompt },
                new { role = "assistant", content = "Understood. I will follow this format and ground my analysis in the actual diffs." },
                new { role = "user", content = prDataContent }
            };

            // Set input as JSON string for Langfuse
            llmSpan?.SetTag("gen_ai.prompt", JsonSerializer.Serialize(messages));
            llmSpan?.SetTag("gen_ai.usage.prompt_tokens", totalPromptLength / 4);

            var llmStopwatch = Stopwatch.StartNew();
            var response = await _chatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            llmStopwatch.Stop();
            var rawResponse = response.Content ?? throw new InvalidOperationException("Empty response from LLM");

            // Track output for Langfuse using OpenTelemetry semantic conventions
            llmSpan?.SetTag("gen_ai.completion", rawResponse);
            llmSpan?.SetTag("gen_ai.usage.completion_tokens", rawResponse.Length / 4);
            llmSpan?.SetTag("gen_ai.usage.total_tokens", (totalPromptLength + rawResponse.Length) / 4);
            llmSpan?.SetTag("gen_ai.response.finish_reasons", new[] { "stop" });
            llmSpan?.SetStatus(ActivityStatusCode.Ok);

            // Calculate metrics
            var outputTokens = rawResponse.Length / 4;
            var totalTokens = (totalPromptLength + rawResponse.Length) / 4;
            var cost = EstimateCost(totalTokens, _modelName);

            TelemetryService.AddEvent("llm_call.complete", new Dictionary<string, object?>
            {
                ["latency_ms"] = llmStopwatch.ElapsedMilliseconds,
                ["output_tokens"] = outputTokens,
                ["total_tokens"] = totalTokens,
                ["cost_usd"] = cost,
                ["response_length"] = rawResponse.Length
            });

            activity?.SetTag("llm.output_tokens", outputTokens);
            activity?.SetTag("llm.total_tokens", totalTokens);
            activity?.SetTag("llm.latency_ms", llmStopwatch.ElapsedMilliseconds);
            activity?.SetTag("llm.cost_usd", cost);

            // Parse the response
            var result = ParseResponse(rawResponse, pr);

            // Validate response
            var actualFiles = pr.ChangedFiles.Select(f => f.Filename).ToArray();
            var validationResult = ValidateResponse(result, actualFiles, pr);

            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Analysis contains unverified content - Issues: {Issues}",
                    string.Join(", ", validationResult.Issues));

                TelemetryService.AddEvent("validation.failed", new Dictionary<string, object?>
                {
                    ["issues"] = string.Join(", ", validationResult.Issues),
                    ["issue_count"] = validationResult.Issues.Count
                });

                activity?.SetTag("validation.passed", false);
            }
            else
            {
                activity?.SetTag("validation.passed", true);
            }

            stopwatch.Stop();

            // Final metrics
            activity?.SetTag("analysis.duration_ms", stopwatch.ElapsedMilliseconds);
            activity?.SetTag("analysis.change_units", result.ChangeUnits.Count);
            activity?.SetTag("analysis.confidence_score", result.ConfidenceScore);

            _logger.LogInformation(
                "Analysis completed - Duration: {Duration}ms, LLM: {LLMDuration}ms, " +
                "Tokens: ~{Tokens}, Cost: ~${Cost:F4}, ChangeUnits: {ChangeUnits}, " +
                "Confidence: {Confidence:F2}, Valid: {Valid}",
                stopwatch.ElapsedMilliseconds,
                llmStopwatch.ElapsedMilliseconds,
                totalTokens,
                cost,
                result.ChangeUnits.Count,
                result.ConfidenceScore,
                validationResult.IsValid);

            TelemetryService.AddEvent("analysis.complete", new Dictionary<string, object?>
            {
                ["success"] = true,
                ["duration_ms"] = stopwatch.ElapsedMilliseconds
            });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis failed after {Duration}ms", stopwatch.ElapsedMilliseconds);

            TelemetryService.RecordException(ex);
            TelemetryService.AddEvent("analysis.failed", new Dictionary<string, object?>
            {
                ["error"] = ex.Message,
                ["duration_ms"] = stopwatch.ElapsedMilliseconds
            });

            throw;
        }
    }

    private string BuildPRDataContent(PullRequestData pr)
    {
        var content = new StringBuilder();
        content.AppendLine("Analyze this pull request and provide a structured JSON response:");
        content.AppendLine();
        content.AppendLine($"Title: {pr.Title}");
        content.AppendLine($"Author: {pr.Author}");
        content.AppendLine($"Description: {pr.Description ?? "No description provided"}");
        content.AppendLine();
        content.AppendLine("Commits:");
        foreach (var commit in pr.Commits.Take(20))
        {
            content.AppendLine($"- {commit.Message}");
        }
        if (pr.Commits.Count > 20)
        {
            content.AppendLine($"... and {pr.Commits.Count - 20} more commits");
        }
        content.AppendLine();
        content.AppendLine("File Changes:");
        foreach (var file in pr.ChangedFiles)
        {
            var truncatedPatch = _chunker.TruncatePatch(file.Patch, 2000);
            content.AppendLine($"\n--- {file.Filename} ({file.Status}) ---");
            content.AppendLine(truncatedPatch);
        }

        return content.ToString();
    }

    private string GetDefaultFewShotExample()
    {
        return """
            Example PR: "Fix authentication bug in login endpoint"
            Files: auth/login.py with diff showing null check added

            Expected JSON response:
            {
              "executive_summary": [
                "Added null check to prevent authentication bypass",
                "Fixes critical security vulnerability in login endpoint"
              ],
              "change_units": [{
                "type": "bugfix",
                "title": "Fix null user authentication bypass",
                "description": "Added validation to check if user object is null before authentication",
                "inferred_intent": "Prevent authentication bypass when user is null",
                "confidence_level": "high",
                "evidence": "if user is None: return unauthorized()",
                "rationale": "Direct evidence of null check in diff at line 45",
                "affected_files": ["auth/login.py"],
                "test_coverage_signal": "none"
              }],
              "risks_and_concerns": ["No tests added for this security fix"],
              "claimed_vs_actual": {
                "alignment_assessment": "aligned",
                "discrepancies": []
              }
            }
            """;
    }

    private static double EstimateCost(int tokens, string model)
    {
        var costPer1MTokens = model switch
        {
            var m when m.Contains("claude-3.5-sonnet") => 3.00,
            var m when m.Contains("gpt-4o") => 5.00,
            var m when m.Contains("gemini") => 0.15,
            var m when m.Contains("llama") => 0.18,
            _ => 1.00
        };

        return (tokens / 1_000_000.0) * costPer1MTokens;
    }

    private AnalysisResult ParseResponse(string rawResponse, PullRequestData pr)
    {
        try
        {
            // Clean the response if wrapped in markdown
            var jsonString = rawResponse;
            if (rawResponse.Contains("```json"))
            {
                var start = rawResponse.IndexOf("```json") + 7;
                var end = rawResponse.LastIndexOf("```");
                if (end > start)
                {
                    jsonString = rawResponse.Substring(start, end - start).Trim();
                }
            }
            else if (rawResponse.Contains("```"))
            {
                var start = rawResponse.IndexOf("```") + 3;
                var end = rawResponse.LastIndexOf("```");
                if (end > start)
                {
                    jsonString = rawResponse.Substring(start, end - start).Trim();
                }
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            var result = JsonSerializer.Deserialize<AnalysisResult>(jsonString, options)
                        ?? throw new InvalidOperationException("Failed to deserialize analysis result");

            // Set metadata
            result.AnalysisTimestamp = DateTime.UtcNow;
            result.PrNumber = pr.Number;
            result.PrTitle = pr.Title;

            // Calculate confidence score
            if (result.ChangeUnits.Any())
            {
                var confidenceMap = new Dictionary<string, double>
                {
                    ["high"] = 0.95,
                    ["medium"] = 0.75,
                    ["low"] = 0.50
                };

                var scores = result.ChangeUnits
                    .Select(cu => confidenceMap.GetValueOrDefault(cu.ConfidenceLevel?.ToLower() ?? "low", 0.50))
                    .ToList();

                result.ConfidenceScore = scores.Average();
            }
            else
            {
                result.ConfidenceScore = 0.50;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse LLM response");
            throw new InvalidOperationException("Failed to parse analysis response", ex);
        }
    }

    private ValidationResult ValidateResponse(AnalysisResult result, string[] actualFiles, PullRequestData pr)
    {
        var issues = new List<string>();

        // Check if referenced files exist
        foreach (var changeUnit in result.ChangeUnits)
        {
            foreach (var file in changeUnit.AffectedFiles)
            {
                if (!actualFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                {
                    issues.Add($"Referenced non-existent file: {file}");
                }
            }
        }

        // Check coverage
        var referencedFiles = result.ChangeUnits
            .SelectMany(cu => cu.AffectedFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet();

        var uncoveredFiles = actualFiles
            .Where(f => !referencedFiles.Contains(f))
            .ToList();

        if (uncoveredFiles.Count > actualFiles.Length * 0.3)
        {
            issues.Add($"Analysis missed {uncoveredFiles.Count}/{actualFiles.Length} files");
        }

        return new ValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues
        };
    }

    private class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Issues { get; set; } = new();
    }
}