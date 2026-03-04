using PullRequestAnalyzer.Configuration;
using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services;

public interface IValidationService
{
    ValidationResult ValidateAnalysis(AnalysisResult result, PullRequestData pr);
}

public class ValidationService : IValidationService
{
    private readonly ValidationSettings _settings;
    private readonly ILogger<ValidationService> _logger;

    public ValidationService(
        IConfiguration configuration,
        ILogger<ValidationService> logger)
    {
        _settings = configuration.GetSection("Analysis:Validation").Get<ValidationSettings>()
                    ?? new ValidationSettings();
        _logger = logger;
    }

    public ValidationResult ValidateAnalysis(AnalysisResult result, PullRequestData pr)
    {
        var issues = new List<string>();
        var actualFiles = pr.ChangedFiles.Select(f => f.Filename).ToArray();

        ValidateAffectedFiles(result, actualFiles, issues);
        ValidateCoverage(result, actualFiles, issues);

        var isValid = issues.Count == 0;

        if (!isValid)
        {
            _logger.LogWarning("Analysis validation failed - Issues: {Issues}",
                string.Join(", ", issues));
        }

        return new ValidationResult
        {
            IsValid = isValid,
            Issues = issues
        };
    }

    private void ValidateAffectedFiles(
        AnalysisResult result,
        string[] actualFiles,
        List<string> issues)
    {
        foreach (var changeUnit in result.ChangeUnits)
        {
            foreach (var file in changeUnit.AffectedFiles)
            {
                if (!actualFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                {
                    issues.Add($"Referenced non-existent file: {file}");
                }
            }
        }
    }

    private void ValidateCoverage(
        AnalysisResult result,
        string[] actualFiles,
        List<string> issues)
    {
        var referencedFiles = result.ChangeUnits
            .SelectMany(cu => cu.AffectedFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet();

        var uncoveredFiles = actualFiles
            .Where(f => !referencedFiles.Contains(f))
            .ToList();

        var uncoveredRatio = (double)uncoveredFiles.Count / actualFiles.Length;

        if (uncoveredRatio > _settings.MaxMissingFilesRatio)
        {
            issues.Add($"Analysis missed {uncoveredFiles.Count}/{actualFiles.Length} files");
        }
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Issues { get; set; } = new();
}