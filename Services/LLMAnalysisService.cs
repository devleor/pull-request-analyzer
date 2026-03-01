using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services;

public sealed class LLMAnalysisService : IAnalysisService
{
    private const string ApiUrl = "https://openrouter.ai/api/v1/chat/completions";

    private readonly HttpClient          _http;
    private readonly DiffChunkingService _chunker;
    private readonly string              _model;
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
        _logger.LogInformation("Starting LLM analysis for PR {Id}", pr.ToIdentifier());

        var prompt   = BuildPrompt(pr);
        var response = await CallApiAsync(prompt);
        var result   = ParseResponse(response, pr);

        _logger.LogInformation("LLM analysis completed for PR {Id}", pr.ToIdentifier());
        return result;
    }

    private string BuildPrompt(PullRequestData pr)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an expert code reviewer. Analyze this GitHub pull request and return ONLY valid JSON.");
        sb.AppendLine();
        sb.AppendLine($"Title: {pr.Title}");
        sb.AppendLine($"Description: {pr.Description}");
        sb.AppendLine($"Author: {pr.Author}");
        sb.AppendLine();
        sb.AppendLine("Commits:");
        foreach (var c in pr.Commits)
            sb.AppendLine($"  {c.Sha[..7]}: {c.Message}");

        sb.AppendLine();
        sb.AppendLine("Changed Files:");
        foreach (var f in pr.ChangedFiles)
        {
            sb.AppendLine($"  {f.Filename} ({f.Status}) +{f.Additions}/-{f.Deletions}");
            if (!string.IsNullOrEmpty(f.Patch))
                sb.AppendLine(_chunker.TruncatePatch(f.Patch, 2000));
        }

        sb.AppendLine();
        sb.AppendLine("""
            Return JSON with this exact structure:
            {
              "executive_summary": ["..."],
              "change_units": [{
                "type": "feature|bugfix|refactor|test|documentation|performance",
                "title": "...",
                "description": "...",
                "affected_files": ["..."],
                "inferred_intent": "...",
                "confidence_level": "high|medium|low",
                "test_coverage_signal": "high|medium|low|none"
              }],
              "risks_and_concerns": ["..."],
              "claimed_vs_actual": {
                "alignment_assessment": "aligned|partially_aligned|misaligned",
                "discrepancies": ["..."]
              }
            }
            """);

        return sb.ToString();
    }

    private async Task<string> CallApiAsync(string prompt)
    {
        var body = JsonSerializer.Serialize(new
        {
            model       = _model,
            messages    = new[] { new { role = "user", content = prompt } },
            temperature = 0.3,
            max_tokens  = 4000
        });

        var response = await _http.PostAsync(ApiUrl,
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
            AnalysisTimestamp = DateTime.UtcNow,
            ConfidenceScore   = 0.85
        };

        if (node["executive_summary"] is JsonArray summary)
            result.ExecutiveSummary = summary.Select(s => s!.GetValue<string>()).ToList();

        if (node["change_units"] is JsonArray units)
        {
            result.ChangeUnits = units.Select(u => new ChangeUnit
            {
                Id                 = Guid.NewGuid().ToString()[..8],
                Type               = u!["type"]?.GetValue<string>()               ?? string.Empty,
                Title              = u["title"]?.GetValue<string>()               ?? string.Empty,
                Description        = u["description"]?.GetValue<string>()         ?? string.Empty,
                InferredIntent     = u["inferred_intent"]?.GetValue<string>()     ?? string.Empty,
                ConfidenceLevel    = u["confidence_level"]?.GetValue<string>()    ?? "medium",
                TestCoverageSignal = u["test_coverage_signal"]?.GetValue<string>() ?? "none",
                AffectedFiles      = u["affected_files"] is JsonArray files
                    ? files.Select(f => f!.GetValue<string>()).ToList()
                    : []
            }).ToList();
        }

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
