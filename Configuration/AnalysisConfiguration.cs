namespace PullRequestAnalyzer.Configuration;

public class AnalysisConfiguration
{
    public LlmSettings Llm { get; set; } = new();
    public CacheSettings Cache { get; set; } = new();
    public ProcessingSettings Processing { get; set; } = new();
    public ValidationSettings Validation { get; set; } = new();
}

public class LlmSettings
{
    public int MaxTokens { get; set; } = 4096;
    public int PromptTokensEstimate { get; set; } = 4; // chars per token
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);
    public double Temperature { get; set; } = 0.2;
    public Dictionary<string, double> ModelCosts { get; set; } = new()
    {
        ["claude-3.5-sonnet"] = 3.00,
        ["gpt-4o"] = 5.00,
        ["gemini"] = 0.15,
        ["llama"] = 0.18,
        ["default"] = 1.00
    };
}

public class CacheSettings
{
    public TimeSpan PullRequestTtl { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan AnalysisTtl { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan JobTtl { get; set; } = TimeSpan.FromHours(24);
}

public class ProcessingSettings
{
    public int MaxPatchLength { get; set; } = 2000;
    public int MaxLogLength { get; set; } = 500;
    public int MaxCommitsToShow { get; set; } = 20;
    public int MaxFilesToShow { get; set; } = 5;
}

public class ValidationSettings
{
    public double MinConfidenceScore { get; set; } = 0.50;
    public double MaxMissingFilesRatio { get; set; } = 0.3;
    public Dictionary<string, double> ConfidenceLevels { get; set; } = new()
    {
        ["high"] = 0.95,
        ["medium"] = 0.75,
        ["low"] = 0.50
    };
}