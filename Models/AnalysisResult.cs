using System.Text.Json.Serialization;

namespace PullRequestAnalyzer.Models;

public sealed class AnalysisResult
{
    [JsonPropertyName("pr_number")]          public int    PrNumber          { get; set; }
    [JsonPropertyName("pr_title")]           public string PrTitle           { get; set; } = string.Empty;
    [JsonPropertyName("analysis_timestamp")] public DateTime AnalysisTimestamp { get; set; }
    [JsonPropertyName("confidence_score")]   public double ConfidenceScore   { get; set; }
    [JsonPropertyName("analysis_notes")]     public string AnalysisNotes     { get; set; } = string.Empty;

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
    [JsonPropertyName("test_coverage_signal")] public string TestCoverageSignal { get; set; } = string.Empty;
    [JsonPropertyName("lines_added")]          public int    LinesAdded         { get; set; }
    [JsonPropertyName("lines_deleted")]        public int    LinesDeleted       { get; set; }

    [JsonPropertyName("affected_files")]
    public List<string> AffectedFiles { get; set; } = [];
}

public sealed class ClaimedVsActual
{
    [JsonPropertyName("claimed_changes")]      public string ClaimedChanges      { get; set; } = string.Empty;
    [JsonPropertyName("actual_changes")]       public string ActualChanges       { get; set; } = string.Empty;
    [JsonPropertyName("alignment_assessment")] public string AlignmentAssessment { get; set; } = string.Empty;

    [JsonPropertyName("discrepancies")]
    public List<string> Discrepancies { get; set; } = [];

    [JsonPropertyName("additional_work_found")]
    public List<string> AdditionalWorkFound { get; set; } = [];
}
