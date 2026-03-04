namespace PullRequestAnalyzer.Models;

/// <summary>
/// Value object for Pull Request ID
/// </summary>
public record PullRequestId
{
    public string Owner { get; }
    public string Repo { get; }
    public int Number { get; }

    public PullRequestId(string owner, string repo, int number)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be empty", nameof(owner));
        if (string.IsNullOrWhiteSpace(repo))
            throw new ArgumentException("Repo cannot be empty", nameof(repo));
        if (number <= 0)
            throw new ArgumentException("PR number must be positive", nameof(number));

        Owner = owner;
        Repo = repo;
        Number = number;
    }

    public override string ToString() => $"{Owner}/{Repo}#{Number}";

    public string ToIdentifier() => $"{Owner}/{Repo}/{Number}";
}

/// <summary>
/// Value object for Confidence Score
/// </summary>
public record ConfidenceScore
{
    private readonly double _value;

    public double Value => _value;

    public ConfidenceScore(double value)
    {
        if (value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(nameof(value),
                "Confidence score must be between 0 and 1");

        _value = value;
    }

    public static ConfidenceScore Low => new(0.50);
    public static ConfidenceScore Medium => new(0.75);
    public static ConfidenceScore High => new(0.95);

    public string ToLevel()
    {
        return _value switch
        {
            >= 0.85 => "high",
            >= 0.65 => "medium",
            _ => "low"
        };
    }

    public static implicit operator double(ConfidenceScore score) => score._value;
    public static implicit operator ConfidenceScore(double value) => new(value);
}

/// <summary>
/// Value object for Job Status
/// </summary>
public record JobStatus
{
    private readonly string _value;

    public string Value => _value;

    private JobStatus(string value)
    {
        _value = value;
    }

    public static JobStatus Queued => new("queued");
    public static JobStatus Processing => new("processing");
    public static JobStatus Completed => new("completed");
    public static JobStatus Failed => new("failed");

    public static JobStatus Parse(string value)
    {
        return value?.ToLower() switch
        {
            "queued" => Queued,
            "processing" => Processing,
            "completed" => Completed,
            "failed" => Failed,
            _ => throw new ArgumentException($"Invalid job status: {value}")
        };
    }

    public override string ToString() => _value;
}

/// <summary>
/// Value object for Change Unit Type
/// </summary>
public record ChangeUnitType
{
    private readonly string _value;

    public string Value => _value;

    private ChangeUnitType(string value)
    {
        _value = value;
    }

    public static ChangeUnitType Feature => new("feature");
    public static ChangeUnitType BugFix => new("bugfix");
    public static ChangeUnitType Refactor => new("refactor");
    public static ChangeUnitType Test => new("test");
    public static ChangeUnitType Documentation => new("docs");
    public static ChangeUnitType Performance => new("performance");
    public static ChangeUnitType Security => new("security");
    public static ChangeUnitType Style => new("style");

    public static ChangeUnitType Parse(string value)
    {
        return value?.ToLower() switch
        {
            "feature" => Feature,
            "bugfix" or "fix" => BugFix,
            "refactor" => Refactor,
            "test" => Test,
            "docs" or "documentation" => Documentation,
            "performance" or "perf" => Performance,
            "security" or "sec" => Security,
            "style" => Style,
            _ => throw new ArgumentException($"Invalid change unit type: {value}")
        };
    }

    public override string ToString() => _value;
}

/// <summary>
/// Value object for Alignment Assessment
/// </summary>
public record AlignmentAssessment
{
    private readonly string _value;

    public string Value => _value;

    private AlignmentAssessment(string value)
    {
        _value = value;
    }

    public static AlignmentAssessment Aligned => new("aligned");
    public static AlignmentAssessment PartiallyAligned => new("partially_aligned");
    public static AlignmentAssessment Misaligned => new("misaligned");

    public static AlignmentAssessment Parse(string value)
    {
        return value?.ToLower() switch
        {
            "aligned" => Aligned,
            "partially_aligned" => PartiallyAligned,
            "misaligned" => Misaligned,
            _ => throw new ArgumentException($"Invalid alignment assessment: {value}")
        };
    }

    public override string ToString() => _value;
}

/// <summary>
/// Value object for Trace ID
/// </summary>
public record TraceId
{
    public string Value { get; }

    public TraceId(string? value = null)
    {
        Value = !string.IsNullOrWhiteSpace(value)
            ? value
            : Guid.NewGuid().ToString();
    }

    public override string ToString() => Value;

    public static implicit operator string(TraceId id) => id.Value;
}