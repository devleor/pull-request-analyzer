using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace PullRequestAnalyzer.Models
{
    /// <summary>
    /// Represents the structured analysis result of a PR using LLM.
    /// </summary>
    public class AnalysisResult
    {
        [JsonProperty("pr_number")]
        public int PrNumber { get; set; }

        [JsonProperty("pr_title")]
        public string PrTitle { get; set; }

        [JsonProperty("analysis_timestamp")]
        public DateTime AnalysisTimestamp { get; set; }

        [JsonProperty("executive_summary")]
        public List<string> ExecutiveSummary { get; set; } = new List<string>();

        [JsonProperty("change_units")]
        public List<ChangeUnit> ChangeUnits { get; set; } = new List<ChangeUnit>();

        [JsonProperty("risks_and_concerns")]
        public List<string> RisksAndConcerns { get; set; } = new List<string>();

        [JsonProperty("claimed_vs_actual")]
        public ClaimedVsActual ClaimedVsActual { get; set; } = new ClaimedVsActual();

        [JsonProperty("confidence_score")]
        public double ConfidenceScore { get; set; }

        [JsonProperty("analysis_notes")]
        public string AnalysisNotes { get; set; }
    }

    /// <summary>
    /// Represents a single unit of change (feature, fix, refactor, etc.)
    /// </summary>
    public class ChangeUnit
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; } // "feature", "bugfix", "refactor", "test", "documentation", "performance", etc.

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("affected_files")]
        public List<string> AffectedFiles { get; set; } = new List<string>();

        [JsonProperty("inferred_intent")]
        public string InferredIntent { get; set; }

        [JsonProperty("confidence_level")]
        public string ConfidenceLevel { get; set; } // "high", "medium", "low"

        [JsonProperty("rationale")]
        public string Rationale { get; set; }

        [JsonProperty("lines_added")]
        public int LinesAdded { get; set; }

        [JsonProperty("lines_deleted")]
        public int LinesDeleted { get; set; }

        [JsonProperty("test_coverage_signal")]
        public string TestCoverageSignal { get; set; } // "high", "medium", "low", "none"
    }

    /// <summary>
    /// Comparison between what PR claims vs what diffs actually show
    /// </summary>
    public class ClaimedVsActual
    {
        [JsonProperty("claimed_changes")]
        public string ClaimedChanges { get; set; }

        [JsonProperty("actual_changes")]
        public string ActualChanges { get; set; }

        [JsonProperty("alignment_assessment")]
        public string AlignmentAssessment { get; set; } // "aligned", "partially_aligned", "misaligned"

        [JsonProperty("discrepancies")]
        public List<string> Discrepancies { get; set; } = new List<string>();

        [JsonProperty("additional_work_found")]
        public List<string> AdditionalWorkFound { get; set; } = new List<string>();
    }
}
