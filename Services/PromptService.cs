using System.Text;
using PullRequestAnalyzer.Configuration;
using PullRequestAnalyzer.Models;
using StackExchange.Redis;

namespace PullRequestAnalyzer.Services;

public interface IPromptService
{
    Task<string> GetSystemPromptAsync();
    string BuildPullRequestContent(PullRequestData pr);
}

public class PromptService : IPromptService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly DiffChunkingService _chunker;
    private readonly ProcessingSettings _settings;
    private readonly ILogger<PromptService> _logger;

    private const string SystemPromptKey = "prompt:pr_analysis_system";
    private const string DefaultSystemPrompt = @"You are an expert code reviewer analyzing a GitHub pull request. Provide a structured JSON analysis following this EXACT schema:

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
      ""affected_files"": [""list of file paths""],
      ""test_coverage_signal"": ""tests_added|tests_modified|no_tests""
    }
  ],
  ""risks_and_concerns"": [""List of identified risks""],
  ""claimed_vs_actual"": {
    ""alignment_assessment"": ""aligned|partially_aligned|misaligned"",
    ""discrepancies"": [""List of discrepancies if any""]
  }
}

CRITICAL REQUIREMENTS:
1. ""change_units"" MUST be an ARRAY of objects, even if there's only one change unit
2. Create SEPARATE change_units for:
   - Different types of changes (feature vs docs vs tests)
   - Different modules or components
   - Different functional areas
3. For a PR with multiple files, you should typically have MULTIPLE change_units
4. Each change_unit should group related files that form a logical change
5. ""affected_files"" must list the ACTUAL file paths from the PR
6. Example of CORRECT format: ""change_units"": [{...}, {...}, {...}]
7. Example of WRONG format: ""change_units"": {...}
8. If you see 10+ files changed, there should be at least 2-3 change_units
9. Group files logically (e.g., all test files in one unit, all docs in another)";

    public PromptService(
        IConnectionMultiplexer redis,
        DiffChunkingService chunker,
        IConfiguration configuration,
        ILogger<PromptService> logger)
    {
        _redis = redis;
        _chunker = chunker;
        _settings = configuration.GetSection("Analysis:Processing").Get<ProcessingSettings>()
                    ?? new ProcessingSettings();
        _logger = logger;
    }

    public async Task<string> GetSystemPromptAsync()
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(SystemPromptKey);

        if (value.IsNullOrEmpty)
        {
            _logger.LogInformation("Using default system prompt");
            return DefaultSystemPrompt;
        }

        return value.ToString()!;
    }

    public string BuildPullRequestContent(PullRequestData pr)
    {
        var content = new StringBuilder();

        AppendHeader(content, pr);
        AppendFiles(content, pr);
        AppendCommits(content, pr);
        AppendDetailedChanges(content, pr);

        return content.ToString();
    }

    private void AppendHeader(StringBuilder content, PullRequestData pr)
    {
        content.AppendLine("Analyze this pull request and provide a structured JSON response:");
        content.AppendLine();
        content.AppendLine($"Title: {pr.Title}");
        content.AppendLine($"Author: {pr.Author}");
        content.AppendLine($"Description: {pr.Description ?? "No description provided"}");
        content.AppendLine();
    }

    private void AppendFiles(StringBuilder content, PullRequestData pr)
    {
        content.AppendLine("Files Changed in this PR:");

        foreach (var file in pr.ChangedFiles)
        {
            content.AppendLine($"- {file.Filename} ({file.Status})");
        }

        content.AppendLine();
    }

    private void AppendCommits(StringBuilder content, PullRequestData pr)
    {
        content.AppendLine("Commits:");

        var commitsToShow = pr.Commits.Take(_settings.MaxCommitsToShow);
        foreach (var commit in commitsToShow)
        {
            content.AppendLine($"- {commit.Message}");
        }

        if (pr.Commits.Count > _settings.MaxCommitsToShow)
        {
            content.AppendLine($"... and {pr.Commits.Count - _settings.MaxCommitsToShow} more commits");
        }

        content.AppendLine();
    }

    private void AppendDetailedChanges(StringBuilder content, PullRequestData pr)
    {
        content.AppendLine("Detailed File Changes with Diffs:");

        foreach (var file in pr.ChangedFiles)
        {
            var truncatedPatch = _chunker.TruncatePatch(file.Patch, _settings.MaxPatchLength);
            content.AppendLine($"\n=== FILE: {file.Filename} (STATUS: {file.Status}) ===");
            content.AppendLine(truncatedPatch);
        }
    }
}