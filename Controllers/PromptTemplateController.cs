using Microsoft.AspNetCore.Mvc;
using PullRequestAnalyzer.Services;

namespace PullRequestAnalyzer.Controllers;

/// <summary>
/// Controller for managing prompt templates stored in Redis
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PromptTemplateController : ControllerBase
{
    private readonly IPromptTemplateService _promptService;
    private readonly ILogger<PromptTemplateController> _logger;

    public PromptTemplateController(
        IPromptTemplateService promptService,
        ILogger<PromptTemplateController> logger)
    {
        _promptService = promptService;
        _logger = logger;
    }

    /// <summary>
    /// Get a prompt template by key
    /// </summary>
    [HttpGet("{key}")]
    [ProducesResponseType(typeof(PromptTemplateResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetTemplate(string key, [FromQuery] string? version = null)
    {
        var template = await _promptService.GetPromptTemplateAsync(key, version);

        if (template == null)
        {
            return NotFound(new { error = $"Prompt template '{key}' not found" });
        }

        return Ok(new PromptTemplateResponse
        {
            Key = key,
            Version = version ?? "latest",
            Template = template
        });
    }

    /// <summary>
    /// Save or update a prompt template
    /// </summary>
    [HttpPost("{key}")]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> SaveTemplate(string key, [FromBody] SavePromptRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Template))
        {
            return BadRequest(new { error = "Template content is required" });
        }

        await _promptService.SavePromptTemplateAsync(
            key,
            request.Template,
            request.Version,
            request.Metadata);

        _logger.LogInformation("Saved prompt template: {Key}, Version: {Version}",
            key, request.Version ?? "latest");

        return CreatedAtAction(
            nameof(GetTemplate),
            new { key, version = request.Version },
            new { message = "Template saved successfully" });
    }

    /// <summary>
    /// Get all versions of a prompt template
    /// </summary>
    [HttpGet("{key}/versions")]
    [ProducesResponseType(typeof(VersionListResponse), 200)]
    public async Task<IActionResult> GetVersions(string key)
    {
        var versions = await _promptService.GetPromptVersionsAsync(key);

        return Ok(new VersionListResponse
        {
            Key = key,
            Versions = versions
        });
    }

    /// <summary>
    /// List all prompt templates
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Dictionary<string, string>), 200)]
    public async Task<IActionResult> ListTemplates()
    {
        var templates = await _promptService.GetAllPromptTemplatesAsync();
        return Ok(templates);
    }

    /// <summary>
    /// Initialize default prompt templates (for demo/setup)
    /// </summary>
    [HttpPost("initialize")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> InitializeDefaults()
    {
        // Save default PR analysis SYSTEM prompt (never expires)
        await _promptService.SavePromptTemplateAsync(
            "pr_analysis_system",
            DefaultPrompts.PRAnalysisSystem,
            "v1.0",
            new Dictionary<string, object>
            {
                ["type"] = "system", // Mark as system prompt - never expires
                ["description"] = "Production PR analysis system prompt with anti-hallucination rules",
                ["model"] = "claude-3.5-sonnet",
                ["created_by"] = "system",
                ["purpose"] = "Main system prompt for PR analysis",
                ["last_updated"] = DateTime.UtcNow
            });

        // Save few-shot example prompt (never expires)
        await _promptService.SavePromptTemplateAsync(
            "pr_analysis_fewshot",
            DefaultPrompts.FewShotExample,
            "v1.0",
            new Dictionary<string, object>
            {
                ["type"] = "system", // Mark as system prompt - never expires
                ["description"] = "Few-shot example for PR analysis",
                ["model"] = "any",
                ["created_by"] = "system",
                ["purpose"] = "Example to guide LLM response format"
            });

        // Save default PR summary prompt
        await _promptService.SavePromptTemplateAsync(
            "pr_summary",
            DefaultPrompts.PRSummary,
            "v1.0",
            new Dictionary<string, object>
            {
                ["type"] = "user",
                ["description"] = "Concise PR summary generation",
                ["model"] = "any",
                ["created_by"] = "system"
            });

        // Save commit analysis prompt
        await _promptService.SavePromptTemplateAsync(
            "commit_analysis",
            DefaultPrompts.CommitAnalysis,
            "v1.0",
            new Dictionary<string, object>
            {
                ["type"] = "user",
                ["description"] = "Individual commit message analysis",
                ["model"] = "any",
                ["created_by"] = "system"
            });

        _logger.LogInformation("Initialized default prompt templates with proper expiration settings");

        return Ok(new
        {
            message = "Default prompt templates initialized",
            templates = new[]
            {
                new { name = "pr_analysis_system", type = "system", expires = "never" },
                new { name = "pr_analysis_fewshot", type = "system", expires = "never" },
                new { name = "pr_summary", type = "user", expires = "90 days" },
                new { name = "commit_analysis", type = "user", expires = "90 days" }
            }
        });
    }
}

public class PromptTemplateResponse
{
    public string Key { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
}

public class SavePromptRequest
{
    public string Template { get; set; } = string.Empty;
    public string? Version { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class VersionListResponse
{
    public string Key { get; set; } = string.Empty;
    public List<string> Versions { get; set; } = new();
}

/// <summary>
/// Default prompt templates for initialization
/// </summary>
public static class DefaultPrompts
{
    public const string PRAnalysisSystem = """
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

        ANALYSIS FRAMEWORK:
        - Identify the TYPE of change (feature, bugfix, refactor, test, docs, config, dependency, performance)
        - Describe WHAT changed (specific modifications)
        - Infer WHY it changed (developer intent)
        - Assess RISKS and CONCERNS
        - Compare CLAIMED vs ACTUAL changes

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
              "test_coverage_signal": "high|medium|low|none"
            }
          ],
          "risks_and_concerns": ["identified risks"],
          "claimed_vs_actual": {
            "alignment_assessment": "aligned|partially_aligned|misaligned",
            "discrepancies": ["list of discrepancies if any"]
          }
        }
        """;

    public const string FewShotExample = """
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

    public const string PRSummary = """
        Generate a concise executive summary of this pull request in 2-6 bullet points.

        Focus on:
        - The primary purpose of the changes
        - Key technical modifications
        - Business impact or value
        - Any critical risks or breaking changes

        Keep each bullet point under 100 characters.
        Be specific and avoid generic statements.
        """;

    public const string CommitAnalysis = """
        Analyze this commit message and determine:

        1. Is it following conventional commit format?
        2. Does it clearly describe the change?
        3. Quality score (1-10)
        4. Suggested improvement (if needed)

        Respond in JSON format:
        {
          "follows_convention": boolean,
          "is_clear": boolean,
          "quality_score": number,
          "suggestion": string | null
        }
        """;
}