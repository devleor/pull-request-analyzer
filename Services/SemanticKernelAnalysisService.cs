#pragma warning disable SKEXP0010

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using PullRequestAnalyzer.Models;
using StackExchange.Redis;

namespace PullRequestAnalyzer.Services;

public sealed class SemanticKernelAnalysisService : IAnalysisService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly DiffChunkingService _chunker;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SemanticKernelAnalysisService> _logger;
    private readonly string _modelName;
    private static readonly ActivitySource ActivitySource = new("PullRequestAnalyzer", "1.0.0");

    public SemanticKernelAnalysisService(
        DiffChunkingService chunker,
        IConnectionMultiplexer redis,
        IConfiguration config,
        ILogger<SemanticKernelAnalysisService> logger)
    {
        _chunker = chunker;
        _redis = redis;
        _logger = logger;

        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
                     ?? config["OpenRouter:ApiKey"]
                     ?? throw new InvalidOperationException("OPENROUTER_API_KEY is not configured");

        _modelName = Environment.GetEnvironmentVariable("OPENROUTER_MODEL")
                    ?? config["OpenRouter:Model"]
                    ?? "liquid/lfm-2.5-1.2b-instruct:free";

        var builder = Kernel.CreateBuilder();

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

        _logger.LogInformation("Semantic Kernel service initialized with model: {Model}", _modelName);
    }

    public async Task<AnalysisResult> AnalyzeAsync(PullRequestData pr)
    {
        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString();

        using var activity = ActivitySource.StartActivity(
            $"pr_analysis.{pr.Number}",
            ActivityKind.Internal);

        activity?.SetTag("pr.owner", pr.Owner);
        activity?.SetTag("pr.repo", pr.Repo);
        activity?.SetTag("pr.number", pr.Number);
        activity?.SetTag("pr.title", pr.Title);
        activity?.SetTag("pr.author", pr.Author);
        activity?.SetTag("pr.file_count", pr.ChangedFiles.Count);
        activity?.SetTag("pr.commit_count", pr.Commits.Count);
        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("llm.model", _modelName);

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
            var systemPrompt = await GetSystemPromptAsync()
                              ?? throw new InvalidOperationException("System prompt not found in Redis");

            var prDataContent = BuildPRDataContent(pr);

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(systemPrompt);
            chatHistory.AddUserMessage(prDataContent);

            var totalPromptLength = systemPrompt.Length + prDataContent.Length;

            activity?.SetTag("llm.prompt_tokens_estimate", totalPromptLength / 4);
            activity?.SetTag("llm.max_tokens", 4096);
            activity?.SetTag("llm.temperature", 0.2);

            _logger.LogInformation("PR has {FileCount} files: {FileList}",
                pr.ChangedFiles.Count,
                string.Join(", ", pr.ChangedFiles.Take(5).Select(f => f.Filename)));

            _logger.LogDebug("Conversation constructed - System: {SystemLength} chars, PR Data: {DataLength} chars",
                systemPrompt.Length, prDataContent.Length);

            var executionSettings = new OpenAIPromptExecutionSettings
            {
                Temperature = 0.2,
                MaxTokens = 4096
            };

            using var llmSpan = ActivitySource.StartActivity(
                "generation",
                ActivityKind.Client);

            llmSpan?.SetTag("gen_ai.system", "openrouter");
            llmSpan?.SetTag("gen_ai.request.model", _modelName);
            llmSpan?.SetTag("gen_ai.request.temperature", 0.2);
            llmSpan?.SetTag("gen_ai.request.max_tokens", 4096);
            llmSpan?.SetTag("gen_ai.request.top_p", 1.0);

            var messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = prDataContent }
            };

            llmSpan?.SetTag("gen_ai.prompt", JsonSerializer.Serialize(messages));
            llmSpan?.SetTag("gen_ai.usage.prompt_tokens", totalPromptLength / 4);

            var llmStopwatch = Stopwatch.StartNew();
            var response = await _chatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            llmStopwatch.Stop();
            var rawResponse = response.Content ?? throw new InvalidOperationException("Empty response from LLM");

            llmSpan?.SetTag("gen_ai.completion", rawResponse);
            llmSpan?.SetTag("gen_ai.usage.completion_tokens", rawResponse.Length / 4);
            llmSpan?.SetTag("gen_ai.usage.total_tokens", (totalPromptLength + rawResponse.Length) / 4);
            llmSpan?.SetTag("gen_ai.response.finish_reasons", new[] { "stop" });
            llmSpan?.SetStatus(ActivityStatusCode.Ok);

            var outputTokens = rawResponse.Length / 4;
            var totalTokens = (totalPromptLength + rawResponse.Length) / 4;
            var cost = EstimateCost(totalTokens, _modelName);

            activity?.SetTag("llm.output_tokens", outputTokens);
            activity?.SetTag("llm.total_tokens", totalTokens);
            activity?.SetTag("llm.latency_ms", llmStopwatch.ElapsedMilliseconds);
            activity?.SetTag("llm.cost_usd", cost);

            var result = ParseResponse(rawResponse, pr);

            var actualFiles = pr.ChangedFiles.Select(f => f.Filename).ToArray();
            var validationResult = ValidateResponse(result, actualFiles, pr);

            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Analysis contains unverified content - Issues: {Issues}",
                    string.Join(", ", validationResult.Issues));
                activity?.SetTag("validation.passed", false);
            }
            else
            {
                activity?.SetTag("validation.passed", true);
            }

            stopwatch.Stop();

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

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis failed after {Duration}ms", stopwatch.ElapsedMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private async Task<string?> GetSystemPromptAsync()
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync("prompt:pr_analysis_system");

        if (value.IsNullOrEmpty)
        {
            return @"You are an expert code reviewer analyzing a GitHub pull request. Provide a structured JSON analysis following this exact schema:

{
  ""executive_summary"": [""2-6 bullet points summarizing key changes""],
  ""change_units"": [
    {
      ""type"": ""feature|bugfix|refactor|test|docs|performance|security|style"",
      ""title"": ""Short descriptive title"",
      ""description"": ""What changed"",
      ""inferred_intent"": ""Why it likely changed"",
      ""confidence_level"": ""high|medium|low"",
      ""rationale"": ""Explanation for confidence level"",
      ""evidence"": ""Specific quote from diff"",
      ""affected_files"": [""MUST list the actual file paths from the PR""],
      ""test_coverage_signal"": ""tests_added|tests_modified|no_tests""
    }
  ],
  ""risks_and_concerns"": [""List of identified risks""],
  ""claimed_vs_actual"": {
    ""alignment_assessment"": ""aligned|partially_aligned|misaligned"",
    ""discrepancies"": [""List of discrepancies if any""]
  }
}

IMPORTANT Rules:
1. Base analysis on actual diffs, not just commit messages
2. Every claim must include evidence from the diff
3. Set confidence levels honestly based on evidence strength
4. Identify ALL significant changes across ALL files
5. For each change_unit, MUST populate affected_files with the actual file paths shown in the PR (e.g., ""mindsdb/integrations/handlers/pocketbase/pocketbase_handler.py"")
6. Extract file paths from the ""--- filename (status) ---"" headers in the file changes section
7. Flag any potential risks or concerns";
        }

        return value.ToString();
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

        content.AppendLine("Files Changed in this PR:");
        foreach (var file in pr.ChangedFiles)
        {
            content.AppendLine($"- {file.Filename} ({file.Status})");
        }
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

        content.AppendLine("Detailed File Changes with Diffs:");
        foreach (var file in pr.ChangedFiles)
        {
            var truncatedPatch = _chunker.TruncatePatch(file.Patch, 2000);
            content.AppendLine($"\n=== FILE: {file.Filename} (STATUS: {file.Status}) ===");
            content.AppendLine(truncatedPatch);
        }

        return content.ToString();
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

            result.AnalysisTimestamp = DateTime.UtcNow;
            result.PrNumber = pr.Number;
            result.PrTitle = pr.Title;

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