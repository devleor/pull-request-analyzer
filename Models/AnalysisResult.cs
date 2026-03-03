using System.Text.Json.Serialization;

namespace PullRequestAnalyzer.Models;

public sealed class AnalysisResult
{
    [JsonPropertyName("pr_number")]          public int    PrNumber          { get; set; }
    [JsonPropertyName("pr_title")]           public string PrTitle           { get; set; } = string.Empty;
    [JsonPropertyName("analysis_timestamp")] public DateTime AnalysisTimestamp { get; set; }
    [JsonPropertyName("confidence_score")]   public double ConfidenceScore   { get; set; }

    [JsonPropertyName("executive_summary")]
    public List<string> ExecutiveSummary { get; set; } = [];

    [JsonPropertyName("change_units")]
    public List<ChangeUnit> ChangeUnits { get; set; } = [];

    [JsonPropertyName("risks_and_concerns")]
    public List<string> RisksAndConcerns { get; set; } = [];

    [JsonPropertyName("claimed_vs_actual")]
    public ClaimedVsActual ClaimedVsActual { get; set; } = new();
}

public sealed class ChangeUnit
{
    [JsonPropertyName("id")]                   public string Id                 { get; set; } = string.Empty;
    [JsonPropertyName("type")]                 public string Type               { get; set; } = string.Empty;
    [JsonPropertyName("title")]                public string Title              { get; set; } = string.Empty;
    [JsonPropertyName("description")]          public string Description        { get; set; } = string.Empty;
    [JsonPropertyName("inferred_intent")]      public string InferredIntent     { get; set; } = string.Empty;
    [JsonPropertyName("confidence_level")]     public string ConfidenceLevel    { get; set; } = string.Empty;
    [JsonPropertyName("rationale")]            public string Rationale          { get; set; } = string.Empty;
    [JsonPropertyName("evidence")]             public string Evidence           { get; set; } = string.Empty;
    [JsonPropertyName("test_coverage_signal")] public string TestCoverageSignal { get; set; } = string.Empty;

    [JsonPropertyName("affected_files")]
    public List<string> AffectedFiles { get; set; } = [];
}

public sealed class ClaimedVsActual
{
    [JsonPropertyName("alignment_assessment")] public string AlignmentAssessment { get; set; } = string.Empty;

    [JsonPropertyName("discrepancies")]
    public List<string> Discrepancies { get; set; } = [];
}
