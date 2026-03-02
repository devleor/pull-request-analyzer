using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services;

public sealed class LLMAnalysisService : IAnalysisService
{
    private const string OpenRouterUrl = "https://openrouter.ai/api/v1/chat/completions";

    private static readonly string SystemPrompt = """
        You are an expert engineering analyst specializing in code review and pull request analysis.
        Your role is to analyze GitHub pull requests by examining the actual code diffs — not just the PR description.

        CRITICAL RULES TO PREVENT HALLUCINATION:
        1. ONLY reference files that appear in the "Changed Files" section
        2. ONLY describe changes that are visible in the actual diff patches
        3. Quote specific line numbers and code snippets from the diffs when making claims
        4. If a diff is truncated or missing, explicitly state this limitation
        5. Never invent files, functions, or changes that aren't in the provided diffs
        6. Use confidence levels: "high" (clear in diff), "medium" (partially visible), "low" (inferred)

        Your analysis must be grounded in evidence from the diffs. When you make a claim, it must be traceable
        to a specific file or code change with line numbers.

        Analysis process:
        1. Read the PR metadata (title, description, author).
        2. Read each commit message and note the progression of changes.
        3. Read each changed file's diff carefully, noting line numbers.
        4. Identify distinct logical units of change based on actual diff content.
        5. Assess whether the PR description accurately reflects what the diffs show.
        6. Identify risks: missing tests, large deletions, security-sensitive files, unclear changes.
        7. Produce the final JSON with specific evidence for each claim.

        For each change unit, you MUST provide:
        - The exact filename(s) from the Changed Files list
        - Specific line numbers or code snippets from the diffs
        - Confidence level based on diff visibility

        Always return valid JSON. Do not wrap it in markdown code blocks.
        """;

    private static readonly string FewShotExample = """
        Example input (abbreviated):
        Title: "Fix null reference in user authentication flow"
        Description: "Fixes a crash when users log in without a profile picture"
        Commits: ["abc1234: Add null check for avatar URL", "def5678: Add unit test for null avatar"]
        Files:
          src/auth/UserService.cs (+3/-1): @@ -45,7 +45,9 @@ public User Authenticate(string token) {
        -    return new User { AvatarUrl = profile.AvatarUrl };
        +    return new User { AvatarUrl = profile?.AvatarUrl ?? string.Empty };

          tests/auth/UserServiceTests.cs (+18/-0): @@ -0,0 +1,18 @@ [Fact] public void Authenticate_NullAvatar_ReturnsEmptyString() { ... }

        Example output:
        {
          "executive_summary": [
            "Fixes a null reference exception in the authentication flow when a user has no profile picture",
            "Adds a null-safe accessor for AvatarUrl with a fallback to empty string",
            "Includes a unit test covering the null avatar scenario"
          ],
          "change_units": [
            {
              "type": "bugfix",
              "title": "Null-safe AvatarUrl accessor in UserService",
              "description": "Replaced direct property access with null-conditional operator and empty string fallback",
              "affected_files": ["src/auth/UserService.cs"],
              "inferred_intent": "Prevent NullReferenceException when profile is null or AvatarUrl is missing",
              "confidence_level": "high",
              "test_coverage_signal": "high"
            }
          ],
          "risks_and_concerns": [
            "The fix uses string.Empty as a fallback — callers that check for null AvatarUrl will not detect the missing value"
          ],
          "claimed_vs_actual": {
            "alignment_assessment": "aligned",
            "discrepancies": []
          }
        }
        """;

    private readonly HttpClient                  _http;
    private readonly DiffChunkingService         _chunker;
    private readonly string                      _model;
    private readonly ILogger<LLMAnalysisService> _logger;

    public LLMAnalysisService(
        IHttpClientFactory httpFactory,
        DiffChunkingService chunker,
        IConfiguration config,
        ILogger<LLMAnalysisService> logger)
    {
        _chunker = chunker;
        _logger  = logger;
        _model   = config["OpenRouter:Model"] ?? "anthropic/claude-3.5-sonnet";

        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
                     ?? config["OpenRouter:ApiKey"]
                     ?? throw new InvalidOperationException("OPENROUTER_API_KEY is not configured");

        _http = httpFactory.CreateClient("openrouter");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.Add("X-Title", "PullRequestAnalyzer");
    }

    public async Task<AnalysisResult> AnalyzeAsync(PullRequestData pr)
    {
        using var activity = Telemetry.Source.StartActivity("llm.analyze", ActivityKind.Client);
        activity?.SetTag("pr.owner",         pr.Owner);
        activity?.SetTag("pr.repo",          pr.Repo);
        activity?.SetTag("pr.number",        pr.Number);
        activity?.SetTag("pr.title",         pr.Title);
        activity?.SetTag("llm.model",        _model);
        activity?.SetTag("pr.changed_files", pr.ChangedFiles.Count);
        activity?.SetTag("pr.commits",       pr.Commits.Count);

        _logger.LogInformation("Starting LLM analysis for PR {Id}", pr.ToIdentifier());

        var userPrompt = BuildUserPrompt(pr);
        var messages   = BuildMessages(userPrompt);

        var rawResponse = await CallApiAsync(messages, activity);
        var result      = ParseResponse(rawResponse, pr);

        activity?.SetTag("analysis.alignment",    result.ClaimedVsActual.AlignmentAssessment);
        activity?.SetTag("analysis.change_units", result.ChangeUnits.Count);
        activity?.SetTag("analysis.confidence",   result.ConfidenceScore);

        _logger.LogInformation("LLM analysis completed for PR {Id}", pr.ToIdentifier());
        return result;
    }

    private static object[] BuildMessages(string userPrompt) =>
    [
        new { role = "system",    content = SystemPrompt },
        new { role = "user",      content = FewShotExample },
        new { role = "assistant", content = "Understood. I will follow this format and ground my analysis in the actual diffs." },
        new { role = "user",      content = userPrompt }
    ];

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

                sb.AppendLine("  Diff:");
                sb.AppendLine(truncatedPatch);

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
        sb.AppendLine("Now analyze this pull request. Think step by step, then return the JSON.");

        return sb.ToString();
    }

    private async Task<string> CallApiAsync(object[] messages, Activity? activity)
    {
        using var span = Telemetry.Source.StartActivity("llm.http_call", ActivityKind.Client);
        span?.SetTag("llm.model",    _model);
        span?.SetTag("http.url",     OpenRouterUrl);

        // Log the prompt for Phoenix monitoring
        var systemMsg = messages[0] as dynamic;
        var userMsg = messages[messages.Length - 1] as dynamic;

        span?.SetTag("llm.system_prompt", systemMsg?.content?.ToString() ?? "");
        span?.SetTag("llm.user_prompt", userMsg?.content?.ToString() ?? "");
        span?.SetTag("llm.messages_count", messages.Length);

        var body = JsonSerializer.Serialize(new
        {
            model       = _model,
            messages,
            temperature = 0.2,
            max_tokens  = 4096
        });

        // Track prompt size
        span?.SetTag("llm.request_size_bytes", Encoding.UTF8.GetByteCount(body));

        var stopwatch = Stopwatch.StartNew();
        var response = await _http.PostAsync(OpenRouterUrl,
            new StringContent(body, Encoding.UTF8, "application/json"));
        stopwatch.Stop();

        response.EnsureSuccessStatusCode();

        var json    = await response.Content.ReadAsStringAsync();
        var node    = JsonNode.Parse(json);
        var content = node?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();

        var promptTokens     = node?["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0;
        var completionTokens = node?["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0;
        var totalTokens      = promptTokens + completionTokens;

        // Enhanced telemetry for Phoenix
        span?.SetTag("llm.prompt_tokens",     promptTokens);
        span?.SetTag("llm.completion_tokens", completionTokens);
        span?.SetTag("llm.total_tokens",      totalTokens);
        span?.SetTag("llm.response_time_ms",  stopwatch.ElapsedMilliseconds);
        span?.SetTag("llm.response_content",  content ?? "");
        span?.SetTag("llm.response_size_bytes", Encoding.UTF8.GetByteCount(content ?? ""));

        // Cost estimation (approximate)
        var estimatedCost = (promptTokens * 0.003 + completionTokens * 0.015) / 1000; // Claude 3.5 Sonnet pricing
        span?.SetTag("llm.estimated_cost_usd", estimatedCost);

        activity?.SetTag("llm.prompt_tokens",     promptTokens);
        activity?.SetTag("llm.completion_tokens", completionTokens);
        activity?.SetTag("llm.total_tokens",      totalTokens);
        activity?.SetTag("llm.response_time_ms",  stopwatch.ElapsedMilliseconds);

        _logger.LogInformation("LLM call completed: {Tokens} tokens in {Time}ms (est. ${Cost:F4})",
            totalTokens, stopwatch.ElapsedMilliseconds, estimatedCost);

        return content ?? throw new InvalidOperationException("Empty response from OpenRouter");
    }

    private static AnalysisResult ParseResponse(string responseText, PullRequestData pr)
    {
        var json   = ExtractJson(responseText);
        var node   = JsonNode.Parse(json) ?? throw new InvalidOperationException("Invalid JSON from LLM");
        var result = new AnalysisResult
        {
            PrNumber          = pr.Number,
            PrTitle           = pr.Title,
            AnalysisTimestamp = DateTime.UtcNow
        };

        if (node["executive_summary"] is JsonArray summary)
            result.ExecutiveSummary = summary.Select(s => s!.GetValue<string>()).ToList();

        if (node["change_units"] is JsonArray units)
        {
            result.ChangeUnits = units.Select(u => new ChangeUnit
            {
                Id                 = Guid.NewGuid().ToString()[..8],
                Type               = u!["type"]?.GetValue<string>()                ?? string.Empty,
                Title              = u["title"]?.GetValue<string>()                ?? string.Empty,
                Description        = u["description"]?.GetValue<string>()          ?? string.Empty,
                InferredIntent     = u["inferred_intent"]?.GetValue<string>()      ?? string.Empty,
                ConfidenceLevel    = u["confidence_level"]?.GetValue<string>()     ?? "medium",
                TestCoverageSignal = u["test_coverage_signal"]?.GetValue<string>() ?? "none",
                AffectedFiles      = u["affected_files"] is JsonArray files
                    ? files.Select(f => f!.GetValue<string>()).ToList()
                    : []
            }).ToList();
        }

        result.ConfidenceScore = result.ChangeUnits.Count > 0
            ? result.ChangeUnits
                .Select(u => u.ConfidenceLevel switch { "high" => 1.0, "medium" => 0.6, _ => 0.3 })
                .Average()
            : 0.5;

        if (node["risks_and_concerns"] is JsonArray risks)
            result.RisksAndConcerns = risks.Select(r => r!.GetValue<string>()).ToList();

        if (node["claimed_vs_actual"] is JsonNode cva)
        {
            result.ClaimedVsActual.AlignmentAssessment = cva["alignment_assessment"]?.GetValue<string>() ?? string.Empty;
            if (cva["discrepancies"] is JsonArray d)
                result.ClaimedVsActual.Discrepancies = d.Select(x => x!.GetValue<string>()).ToList();
        }

        return result;
    }

    private static string ExtractJson(string text)
    {
        var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)```");
        if (match.Success) return match.Groups[1].Value.Trim();

        var start = text.IndexOf('{');
        var end   = text.LastIndexOf('}');

        return start >= 0 && end > start
            ? text[start..(end + 1)]
            : throw new InvalidOperationException("Could not extract JSON from LLM response");
    }
}
