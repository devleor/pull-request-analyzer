# Validation Pipeline Documentation

## Overview

The Pull Request Analyzer implements a multi-layer validation pipeline to prevent LLM hallucinations and ensure accurate, evidence-based analysis. This document details the validation mechanisms implemented in the production-ready Semantic Kernel integration.

## Architecture

```
┌─────────────────────────────────────────────────┐
│           LLM Response (Raw JSON)                │
└─────────────────────┬───────────────────────────┘
                      ▼
┌─────────────────────────────────────────────────┐
│     1. JSON Structure Validation                 │
│     SemanticKernelAnalysisService.cs:L180       │
└─────────────────────┬───────────────────────────┘
                      ▼
┌─────────────────────────────────────────────────┐
│     2. File Reference Validation                 │
│     SemanticKernelAnalysisService.cs:L195       │
└─────────────────────┬───────────────────────────┘
                      ▼
┌─────────────────────────────────────────────────┐
│     3. Evidence Validation                       │
│     SemanticKernelAnalysisService.cs:L210       │
└─────────────────────┬───────────────────────────┘
                      ▼
┌─────────────────────────────────────────────────┐
│     4. Confidence Assessment                     │
│     Built into prompt engineering                │
└─────────────────────┬───────────────────────────┘
                      ▼
┌─────────────────────────────────────────────────┐
│        Validated Analysis Result                 │
└─────────────────────────────────────────────────┘
```

## Implementation Details

### 1. Prompt Engineering for Grounding

Located in: `Services/SemanticKernelAnalysisService.cs`

The system prompt enforces evidence-based analysis:

```csharp
private string BuildSystemPrompt()
{
    return @"You are an expert code reviewer analyzing pull request changes.

CRITICAL REQUIREMENTS:
1. ONLY describe changes you can see in the actual diffs
2. Every claim MUST include evidence (exact quotes from diffs)
3. Include confidence_level: 'high' (clear evidence), 'medium' (inferred), 'low' (uncertain)
4. Provide rationale explaining your confidence assessment
5. Never invent or assume changes not visible in the diffs
6. If you cannot determine something from the diffs, explicitly state this

Your analysis must be evidence-based and grounded in the actual code changes.";
}
```

### 2. JSON Response Validation

The service enforces structured JSON output:

```csharp
var executionSettings = new OpenAIPromptExecutionSettings
{
    Temperature = 0.2,  // Low temperature for consistent output
    MaxTokens = 4096,
    ResponseFormat = "json_object"  // Force JSON response
};
```

### 3. File Reference Validation

Validates that all files mentioned by the LLM exist in the PR:

```csharp
private ValidationResult ValidateResponse(
    AnalysisResult result,
    List<ChangedFile> actualFiles,
    PullRequestData pr)
{
    var validationWarnings = new List<string>();
    var actualFileNames = actualFiles.Select(f => f.Filename).ToHashSet();

    // Check each change unit for hallucinated files
    foreach (var unit in result.ChangeUnits)
    {
        foreach (var file in unit.AffectedFiles)
        {
            if (!actualFileNames.Contains(file))
            {
                validationWarnings.Add(
                    $"File '{file}' referenced in analysis but not found in PR");

                _logger.LogWarning(
                    "Potential hallucination: File {File} not in PR {PrNumber}",
                    file, pr.Number);
            }
        }
    }

    return new ValidationResult
    {
        IsValid = validationWarnings.Count == 0,
        Warnings = validationWarnings
    };
}
```

### 4. Evidence Verification

Ensures that evidence citations exist in the actual diffs:

```csharp
private bool VerifyEvidence(string evidence, List<ChangedFile> files)
{
    if (string.IsNullOrWhiteSpace(evidence))
        return false;

    // Check if the evidence appears in any of the file patches
    foreach (var file in files)
    {
        if (file.Patch?.Contains(evidence) == true)
        {
            return true;
        }
    }

    _logger.LogWarning("Evidence not found in diffs: {Evidence}", evidence);
    return false;
}
```

### 5. Confidence Level Enforcement

The model is required to provide confidence levels with rationale:

```json
{
  "change_units": [
    {
      "title": "Null safety improvements",
      "confidence_level": "high",
      "evidence": "@@ -45,8 +45,11 @@ if (user?.Profile != null)",
      "rationale": "Direct evidence of null check operator added in UserService.cs line 45"
    }
  ]
}
```

## Validation Scenarios

### Scenario 1: Valid Analysis

**Input PR**: Bug fix adding null checks
**LLM Output**:
```json
{
  "change_units": [{
    "type": "bugfix",
    "title": "Add null safety check",
    "confidence_level": "high",
    "evidence": "if (user?.Profile != null)",
    "rationale": "Null-conditional operator visible in diff",
    "affected_files": ["Services/UserService.cs"]
  }]
}
```
**Validation Result**: ✅ PASS - File exists, evidence found in diff

### Scenario 2: Hallucinated File

**Input PR**: Changes to UserService.cs only
**LLM Output**:
```json
{
  "affected_files": ["Services/UserService.cs", "Services/AuthService.cs"]
}
```
**Validation Result**: ⚠️ WARNING - AuthService.cs not in PR

### Scenario 3: Missing Evidence

**Input PR**: Simple typo fix
**LLM Output**:
```json
{
  "change_units": [{
    "type": "feature",
    "title": "Add authentication system",
    "confidence_level": "high",
    "evidence": "",
    "rationale": "Authentication implemented"
  }]
}
```
**Validation Result**: ❌ FAIL - No evidence provided for claim

### Scenario 4: Misaligned Claims

**Input PR Description**: "Implement OAuth2 authentication"
**Actual Changes**: README typo fixes
**LLM Output**:
```json
{
  "claimed_vs_actual": {
    "alignment_assessment": "misaligned",
    "discrepancies": [
      "PR claims OAuth2 implementation but only README typos were fixed"
    ]
  }
}
```
**Validation Result**: ✅ PASS - Correctly identified misalignment

## Prompt Engineering Techniques

### 1. Few-Shot Learning

The prompt includes an example of proper analysis:

```csharp
private string GetFewShotExample()
{
    return @"
Example Input:
{
  'changed_files': [{
    'filename': 'user.py',
    'patch': '@@ -10,3 +10,5 @@\n-return user.profile\n+if user and user.profile:\n+    return user.profile\n+return None'
  }]
}

Example Output:
{
  'change_units': [{
    'type': 'bugfix',
    'title': 'Add null safety check for user profile access',
    'confidence_level': 'high',
    'evidence': 'if user and user.profile:',
    'rationale': 'Clear null check added before accessing user.profile',
    'affected_files': ['user.py']
  }]
}";
}
```

### 2. Chain-of-Thought Prompting

The prompt encourages step-by-step analysis:

```text
Analyze the PR by:
1. First, read all file changes
2. Identify distinct logical changes
3. For each change, find supporting evidence
4. Assess your confidence based on evidence clarity
5. Compare claimed vs actual changes
```

### 3. Negative Instructions

Explicit instructions on what NOT to do:

```text
DO NOT:
- Make assumptions about code not shown
- Reference files not in the changed_files list
- Claim changes without quoting evidence
- Use high confidence without clear evidence
```

## Validation Metrics

The system tracks:

1. **Hallucination Rate**: Percentage of analyses with non-existent file references
2. **Evidence Coverage**: Percentage of claims with valid evidence
3. **Confidence Distribution**: Breakdown of high/medium/low confidence assessments
4. **Alignment Accuracy**: How well the system identifies misaligned PRs

### Sample Metrics Logging

```csharp
_logger.LogInformation(
    "Analysis validation complete: " +
    "Hallucinations={HallucinationCount}, " +
    "EvidenceProvided={EvidenceCount}/{TotalClaims}, " +
    "ConfidenceBreakdown=H:{High}/M:{Medium}/L:{Low}",
    validationWarnings.Count,
    evidenceProvidedCount,
    totalClaims,
    highConfidence,
    mediumConfidence,
    lowConfidence
);
```

## Error Handling

### Validation Failures

When validation fails, the system:
1. Logs detailed warnings
2. Returns validation warnings in the response
3. May retry with adjusted prompting (configurable)
4. Adds metadata about validation issues

```csharp
if (!validationResult.IsValid)
{
    result.ValidationWarnings = validationResult.Warnings;
    result.ValidationStatus = "partial";

    _logger.LogWarning(
        "Validation issues for PR {PrNumber}: {Warnings}",
        pr.Number,
        string.Join(", ", validationResult.Warnings)
    );
}
```

## Configuration

### Environment Variables

```bash
# Validation strictness (optional)
VALIDATION_MODE=strict  # strict | moderate | lenient

# Retry on validation failure (optional)
VALIDATION_RETRY_ENABLED=true
VALIDATION_MAX_RETRIES=2
```

### Tuning Parameters

```csharp
public class ValidationConfig
{
    public bool RequireEvidence { get; set; } = true;
    public bool ValidateFiles { get; set; } = true;
    public bool RequireConfidenceRationale { get; set; } = true;
    public double MinimumConfidenceThreshold { get; set; } = 0.7;
}
```

## Testing the Validation Pipeline

### Unit Tests

```csharp
[Fact]
public void ValidateResponse_ShouldDetectHallucinatedFiles()
{
    // Arrange
    var result = new AnalysisResult
    {
        ChangeUnits = new List<ChangeUnit>
        {
            new() { AffectedFiles = ["real.cs", "fake.cs"] }
        }
    };
    var actualFiles = new List<ChangedFile>
    {
        new() { Filename = "real.cs" }
    };

    // Act
    var validation = ValidateResponse(result, actualFiles, pr);

    // Assert
    Assert.False(validation.IsValid);
    Assert.Contains("fake.cs", validation.Warnings[0]);
}
```

### Integration Tests

```bash
# Test with intentionally misaligned PR
curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d @test-requests/misaligned-pr.json | jq '.claimed_vs_actual'

# Should return:
{
  "alignment_assessment": "misaligned",
  "discrepancies": ["PR claims OAuth2 but only README changes found"]
}
```

## Monitoring and Observability

### Validation Metrics Endpoint

```csharp
app.MapGet("/metrics/validation", () => Results.Ok(new
{
    total_analyses = _totalAnalyses,
    validation_failures = _validationFailures,
    hallucination_rate = _hallucinationRate,
    average_confidence = _averageConfidence,
    evidence_coverage = _evidenceCoverage
}));
```

### Logging Pattern

All validation events are logged with structured data:

```csharp
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["PrNumber"] = pr.Number,
    ["ValidationMode"] = "strict",
    ["LlmModel"] = model
}))
{
    _logger.LogInformation("Starting validation pipeline");
    // ... validation logic
}
```

## Best Practices

1. **Always validate LLM outputs** - Never trust raw LLM responses
2. **Log validation failures** - Track patterns of hallucination
3. **Provide feedback to users** - Include validation warnings in API responses
4. **Monitor validation metrics** - Watch for degradation over time
5. **Test with adversarial inputs** - Intentionally misaligned PRs
6. **Update prompts based on failures** - Continuous improvement

## Future Enhancements

1. **Semantic similarity checking** - Use embeddings to verify evidence relevance
2. **Multi-model validation** - Cross-check with different LLMs
3. **Learning from corrections** - Fine-tune based on validation failures
4. **Automated prompt optimization** - A/B test prompt variations
5. **Real-time hallucination detection** - Stream validation during generation

## Conclusion

The validation pipeline ensures that the Pull Request Analyzer provides reliable, evidence-based analysis by:
- Enforcing structured output through Semantic Kernel
- Validating file references against actual PR data
- Requiring evidence citations for all claims
- Assessing confidence with explicit rationale
- Detecting and reporting misaligned PR descriptions

This multi-layer approach minimizes hallucinations and provides engineering leaders with trustworthy insights into their pull requests.