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

        Your analysis must be grounded in evidence from the diffs. When you make a claim, it must be traceable
        to a specific file or code change. Do not infer intent from the PR title alone.

        You will think step by step before producing your final JSON output:
        1. Read the PR metadata (title, description, author).
        2. Read each commit message and note the progression of changes.
        3. Read each changed file's diff carefully.
        4. Identify distinct logical units of change (a "change unit" is a cohesive set of modifications with a single purpose).
        5. Assess whether the PR description accurately reflects what the diffs show.
        6. Identify risks: missing tests, large deletions, security-sensitive files, unclear changes.
        7. Produce the final JSON.

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

    private readonly HttpClient              _http;
    private readonly DiffChunkingService     _chunker;
    private readonly LangfuseService         _langfuse;
    private readonly string                  _model;
    private readonly ILogger<LLMAnalysisService> _logger;

    public LLMAnalysisService(
        IHttpClientFactory httpFactory,
        DiffChunkingService chunker,
        LangfuseService langfuse,
        IConfiguration config,
        ILogger<LLMAnalysisService> logger)
    {
        _chunker  = chunker;
        _langfuse = langfuse;
        _logger   = logger;
        _model    = config["OpenRouter:Model"] ?? "anthropic/claude-3.5-sonnet";

        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
                     ?? config["OpenRouter:ApiKey"]
                     ?? throw new InvalidOperationException("OPENROUTER_API_KEY is not configured");

        _http = httpFactory.CreateClient("openrouter");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.Add("X-Title", "PullRequestAnalyzer");
    }

    public async Task<AnalysisResult> AnalyzeAsync(PullRequestData pr)
    {
        _logger.LogInformation("Starting LLM analysis for PR {Id}", pr.ToIdentifier());

        var traceId   = Guid.NewGuid().ToString();
        var userPrompt = BuildUserPrompt(pr);
        var messages   = BuildMessages(userPrompt);

        var startedAt = DateTime.UtcNow;
        var rawResponse = await CallApiAsync(messages);
        var latencyMs   = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;

        var result = ParseResponse(rawResponse, pr);

        await _langfuse.TraceAsync(new LangfuseTrace(
            TraceId:    traceId,
            Name:       $"analyze-pr-{pr.Owner}/{pr.Repo}#{pr.Number}",
            Input:      userPrompt,
            Output:     rawResponse,
            Model:      _model,
            LatencyMs:  latencyMs,
            Metadata:   new Dictionary<string, object>
            {
                ["pr_number"]      = pr.Number,
                ["pr_title"]       = pr.Title,
                ["changed_files"]  = pr.ChangedFiles.Count,
                ["commits"]        = pr.Commits.Count,
                ["alignment"]      = result.ClaimedVsActual.AlignmentAssessment
            }
        ));

        _logger.LogInformation("LLM analysis completed for PR {Id} in {LatencyMs}ms", pr.ToIdentifier(), latencyMs);
        return result;
    }

    private static object[] BuildMessages(string userPrompt) =>
    [
        new { role = "system", content = SystemPrompt },
        new { role = "user",   content = FewShotExample },
        new { role = "assistant", content = "Understood. I will follow this format and ground my analysis in the actual diffs." },
        new { role = "user",   content = userPrompt }
    ];

    private string BuildUserPrompt(PullRequestData pr)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Title: {pr.Title}");
        sb.AppendLine($"Description: {(string.IsNullOrWhiteSpace(pr.Description) ? "(none)" : pr.Description)}");
        sb.AppendLine($"Author: {pr.Author}");
        sb.AppendLine($"State: {pr.State}");
        sb.AppendLine();

        sb.AppendLine("Commits:");
        foreach (var c in pr.Commits)
            sb.AppendLine($"  {c.Sha[..Math.Min(7, c.Sha.Length)]}: {c.Message.Split('\n')[0]}");

        sb.AppendLine();
        sb.AppendLine("Changed Files:");
        foreach (var f in pr.ChangedFiles)
        {
            sb.AppendLine($"  {f.Filename} ({f.Status}) +{f.Additions}/-{f.Deletions}");
            if (!string.IsNullOrEmpty(f.Patch))
            {
                sb.AppendLine(_chunker.TruncatePatch(f.Patch, 2000));
                sb.AppendLine();
            }
        }

        sb.AppendLine();
        sb.AppendLine("Now analyze this pull request. Think step by step, then return the JSON.");

        return sb.ToString();
    }

    private async Task<string> CallApiAsync(object[] messages)
    {
        var body = JsonSerializer.Serialize(new
        {
            model       = _model,
            messages,
            temperature = 0.2,
            max_tokens  = 4096
        });

        var response = await _http.PostAsync(OpenRouterUrl,
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();

        var json    = await response.Content.ReadAsStringAsync();
        var content = JsonNode.Parse(json)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();

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
