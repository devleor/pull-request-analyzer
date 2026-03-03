#pragma warning disable SKEXP0010 // Suppress experimental API warnings

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services;

public sealed class SemanticKernelAnalysisService : IAnalysisService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly DiffChunkingService _chunker;
    private readonly ILogger<SemanticKernelAnalysisService> _logger;

    public SemanticKernelAnalysisService(
        DiffChunkingService chunker,
        IConfiguration config,
        ILogger<SemanticKernelAnalysisService> logger)
    {
        _chunker = chunker;
        _logger = logger;

        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
                     ?? config["OpenRouter:ApiKey"]
                     ?? throw new InvalidOperationException("OPENROUTER_API_KEY is not configured");

        var model = Environment.GetEnvironmentVariable("OPENROUTER_MODEL")
                    ?? config["OpenRouter:Model"]
                    ?? "google/gemini-2.0-flash-exp:free";

        // Build Semantic Kernel with OpenRouter
        var builder = Kernel.CreateBuilder();

        // Configure for OpenRouter (OpenAI-compatible)
        builder.AddOpenAIChatCompletion(
            modelId: model,
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
    }

    public async Task<AnalysisResult> AnalyzeAsync(PullRequestData pr)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Starting Semantic Kernel analysis for PR {Owner}/{Repo}#{Number}",
            pr.Owner, pr.Repo, pr.Number);

        // Build the analysis prompt
        var userPrompt = BuildUserPrompt(pr);

        // Create chat history with system prompt and few-shot example
        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(GetSystemPrompt());
        chatHistory.AddUserMessage(GetFewShotExample());
        chatHistory.AddAssistantMessage("Understood. I will follow this format and ground my analysis in the actual diffs.");
        chatHistory.AddUserMessage(userPrompt);

        // Configure OpenAI-specific settings for structured output
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0.2,
            MaxTokens = 4096,
            ResponseFormat = "json_object" // Force JSON response
        };

        try
        {
            // Get completion from LLM
            var response = await _chatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var rawResponse = response.Content ?? throw new InvalidOperationException("Empty response from LLM");

            // Parse the response
            var result = ParseResponse(rawResponse, pr);

            // Validate response for hallucinations
            var actualFiles = pr.ChangedFiles.Select(f => f.Filename).ToArray();
            var validationResult = ValidateResponse(result, actualFiles, pr);

            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Analysis contains unverified content for PR {Owner}/{Repo}#{Number}: {Issues}",
                    pr.Owner, pr.Repo, pr.Number, string.Join(", ", validationResult.Issues));
            }

            stopwatch.Stop();
            _logger.LogInformation("Semantic Kernel analysis completed for PR {Owner}/{Repo}#{Number} in {ElapsedMs}ms - " +
                "Change units: {ChangeUnits}, Confidence: {Confidence:F2}, Valid: {IsValid}",
                pr.Owner, pr.Repo, pr.Number, stopwatch.ElapsedMilliseconds,
                result.ChangeUnits.Count, result.ConfidenceScore, validationResult.IsValid);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze PR using Semantic Kernel");
            throw;
        }
    }

    private string GetSystemPrompt()
    {
        return """
            You are an expert code reviewer analyzing pull request changes.

            CRITICAL RULES FOR GROUNDING AND ANTI-HALLUCINATION:
            1. ONLY analyze files and changes that are explicitly shown in the diffs
            2. Every claim MUST include a direct quote from the diff as evidence
            3. If you cannot find evidence in the diff, state "No evidence in diff"
            4. Reference specific line numbers and file paths
            5. Use confidence levels based on evidence strength:
               - HIGH: Direct evidence in the diff (explicit changes visible)
               - MEDIUM: Inferred from context (file names, patterns)
               - LOW: Assumption based on conventions

            You MUST respond with valid JSON following this exact schema:
            {
              "executive_summary": ["2-6 bullet points summarizing the changes"],
              "change_units": [
                {
                  "type": "feature|bugfix|refactor|test|docs|config|dependency|performance",
                  "title": "short descriptive title",
                  "description": "what changed",
                  "inferred_intent": "why it likely changed",
                  "confidence_level": "high|medium|low",
                  "evidence": "exact quote from diff",
                  "rationale": "explanation for confidence level",
                  "affected_files": ["file paths"],
                  "test_coverage_signal": "tests_added|tests_modified|no_tests|unknown"
                }
              ],
              "risks_and_concerns": ["list of risks or empty array"],
              "claimed_vs_actual": {
                "alignment_assessment": "aligned|partially_aligned|misaligned|no_description",
                "discrepancies": ["list of discrepancies or empty array"]
              }
            }
            """;
    }

    private string GetFewShotExample()
    {
        return """
            Analyze this pull request:

            === PULL REQUEST METADATA ===
            Title: Fix null reference exception in UserService
            Description: Fixes crash when user profile is missing
            Author: developer123

            === CHANGED FILES WITH DIFFS ===
            FILE 1/1: src/Services/UserService.cs
              Status: modified
              Changes: +5 lines / -2 lines
              Diff:
            @@ -45,8 +45,11 @@ public class UserService
             public User GetUser(int id)
             {
                 var user = _repository.GetById(id);
            -    var profile = _profileService.GetProfile(user.ProfileId);
            -    user.Profile = profile;
            +    if (user != null)
            +    {
            +        var profile = _profileService.GetProfile(user.ProfileId);
            +        user.Profile = profile ?? new UserProfile();
            +    }
                 return user;
             }

            Return the analysis as JSON.
            """;
    }

    private string BuildUserPrompt(PullRequestData pr)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== PULL REQUEST METADATA ===");
        sb.AppendLine($"Title: {pr.Title}");
        sb.AppendLine($"Description: {(string.IsNullOrWhiteSpace(pr.Description) ? "(none)" : pr.Description)}");
        sb.AppendLine($"Author: {pr.Author}");
        sb.AppendLine($"State: {pr.State}");
        sb.AppendLine($"Total Files Changed: {pr.ChangedFiles.Count}");
        sb.AppendLine($"Total Commits: {pr.Commits.Count}");
        sb.AppendLine();

        sb.AppendLine("=== COMMITS (chronological order) ===");
        foreach (var c in pr.Commits)
            sb.AppendLine($"  {c.Sha[..Math.Min(7, c.Sha.Length)]}: {c.Message.Split('\n')[0]}");

        sb.AppendLine();
        sb.AppendLine("=== CHANGED FILES WITH DIFFS ===");
        sb.AppendLine("IMPORTANT: Base your analysis ONLY on the diffs shown below.");
        sb.AppendLine("If a diff is truncated, mention this limitation in your analysis.");
        sb.AppendLine();

        var fileIndex = 0;
        foreach (var f in pr.ChangedFiles)
        {
            fileIndex++;
            sb.AppendLine($"FILE {fileIndex}/{pr.ChangedFiles.Count}: {f.Filename}");
            sb.AppendLine($"  Status: {f.Status}");
            sb.AppendLine($"  Changes: +{f.Additions} lines / -{f.Deletions} lines");

            if (!string.IsNullOrEmpty(f.Patch))
            {
                var truncatedPatch = _chunker.TruncatePatch(f.Patch, 2000);
                var wasTruncated = truncatedPatch.Length < f.Patch.Length;

                // Sanitize patch content to avoid JSON issues
                var sanitizedPatch = truncatedPatch
                    .Replace("\t", "    ")  // Replace tabs with spaces
                    .Replace("\r", "");     // Remove carriage returns

                sb.AppendLine("  Diff:");
                sb.AppendLine(sanitizedPatch);

                if (wasTruncated)
                {
                    sb.AppendLine($"  [DIFF TRUNCATED - showing {truncatedPatch.Length} of {f.Patch.Length} characters]");
                }
            }
            else
            {
                sb.AppendLine("  [NO DIFF AVAILABLE - file may be binary or too large]");
            }
            sb.AppendLine();
        }

        sb.AppendLine("=== ANALYSIS INSTRUCTIONS ===");
        sb.AppendLine("1. Analyze ONLY the files and changes shown above");
        sb.AppendLine("2. Quote specific line numbers and code from the diffs");
        sb.AppendLine("3. Mark confidence as 'low' if diff is truncated or missing");
        sb.AppendLine("4. Identify discrepancies between PR description and actual changes");
        sb.AppendLine("5. Return a valid JSON response following the schema");
        sb.AppendLine();
        sb.AppendLine("Now analyze this pull request and return the JSON response.");

        return sb.ToString();
    }

    private AnalysisResult ParseResponse(string responseText, PullRequestData pr)
    {
        try
        {
            _logger.LogDebug("Parsing LLM response of length: {Length}", responseText.Length);

            var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            var result = new AnalysisResult
            {
                PrNumber = pr.Number,
                PrTitle = pr.Title,
                AnalysisTimestamp = DateTime.UtcNow
            };

            // Parse executive summary
            if (root.TryGetProperty("executive_summary", out var summary))
            {
                result.ExecutiveSummary = summary.EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .ToList();
            }

            // Parse change units
            if (root.TryGetProperty("change_units", out var units))
            {
                result.ChangeUnits = units.EnumerateArray().Select(u => new ChangeUnit
                {
                    Id = Guid.NewGuid().ToString()[..8],
                    Type = u.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                    Title = u.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                    Description = u.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                    InferredIntent = u.TryGetProperty("inferred_intent", out var intent) ? intent.GetString() ?? "" : "",
                    ConfidenceLevel = u.TryGetProperty("confidence_level", out var conf) ? conf.GetString() ?? "medium" : "medium",
                    Evidence = u.TryGetProperty("evidence", out var ev) ? ev.GetString() ?? "" : "",
                    Rationale = u.TryGetProperty("rationale", out var rat) ? rat.GetString() ?? "" : "",
                    TestCoverageSignal = u.TryGetProperty("test_coverage_signal", out var test) ? test.GetString() ?? "unknown" : "unknown",
                    AffectedFiles = u.TryGetProperty("affected_files", out var files)
                        ? files.EnumerateArray().Select(f => f.GetString() ?? "").ToList()
                        : new List<string>()
                }).ToList();
            }

            // Calculate confidence score
            result.ConfidenceScore = result.ChangeUnits.Count > 0
                ? result.ChangeUnits
                    .Select(u => u.ConfidenceLevel switch { "high" => 1.0, "medium" => 0.6, _ => 0.3 })
                    .Average()
                : 0.5;

            // Parse risks and concerns
            if (root.TryGetProperty("risks_and_concerns", out var risks))
            {
                result.RisksAndConcerns = risks.EnumerateArray()
                    .Select(r => r.GetString() ?? string.Empty)
                    .ToList();
            }

            // Parse claimed vs actual
            if (root.TryGetProperty("claimed_vs_actual", out var cva))
            {
                if (cva.TryGetProperty("alignment_assessment", out var align))
                    result.ClaimedVsActual.AlignmentAssessment = align.GetString() ?? "";

                if (cva.TryGetProperty("discrepancies", out var disc))
                {
                    result.ClaimedVsActual.Discrepancies = disc.EnumerateArray()
                        .Select(d => d.GetString() ?? string.Empty)
                        .ToList();
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse LLM response. Response text (first 500 chars): {Response}",
                responseText.Length > 500 ? responseText[..500] : responseText);
            throw new InvalidOperationException($"Failed to parse LLM response: {ex.Message}", ex);
        }
    }

    private ValidationResult ValidateResponse(AnalysisResult result, string[] actualFiles, PullRequestData pr)
    {
        var issues = new List<string>();
        var referencedFiles = new HashSet<string>();

        // Extract all referenced files from change units
        foreach (var unit in result.ChangeUnits)
        {
            foreach (var file in unit.AffectedFiles)
            {
                referencedFiles.Add(file);
            }

            // Validate evidence exists in PR diffs
            if (!string.IsNullOrEmpty(unit.Evidence))
            {
                var evidenceFound = false;
                foreach (var changedFile in pr.ChangedFiles)
                {
                    if (!string.IsNullOrEmpty(changedFile.Patch) &&
                        changedFile.Patch.Contains(unit.Evidence, StringComparison.Ordinal))
                    {
                        evidenceFound = true;
                        break;
                    }
                }

                if (!evidenceFound && unit.ConfidenceLevel == "high")
                {
                    issues.Add($"Evidence not found in diffs for: {unit.Title}");
                }
            }
        }

        // Check for hallucinated files
        var hallucinatedFiles = referencedFiles.Except(actualFiles).ToList();
        if (hallucinatedFiles.Any())
        {
            issues.Add($"Non-existent files referenced: {string.Join(", ", hallucinatedFiles)}");
        }

        return new ValidationResult
        {
            IsValid = !issues.Any(),
            Issues = issues
        };
    }

    private class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Issues { get; set; } = new();
    }
}