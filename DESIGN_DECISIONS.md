# Design Decisions

## Product Hypothesis

This tool helps engineering leaders understand what was **actually implemented** vs what was **claimed** in pull requests, reducing the risk of miscommunication and ensuring code changes align with intended features.

## Current Architecture (As-Is)

```
┌─────────────────────────────────────────────────────────────────┐
│                         Client Application                       │
└─────────────────────────┬───────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│                    ASP.NET Core Web API                          │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Controllers:                                             │    │
│  │ • AnalyzeController (sync/async analysis)               │    │
│  │ • PullRequestController (GitHub data)                   │    │
│  │ • AsyncAnalysisController (job management)              │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────┬───────────────────────────────────────┘
                          │
            ┌─────────────┴──────────────┬──────────────┐
            ▼                            ▼              ▼
┌──────────────────────┐    ┌──────────────────┐  ┌──────────────┐
│  Semantic Kernel     │    │  GitHub API      │  │    Redis     │
│  Analysis Service    │    │  (Octokit)       │  │  Cache/Queue │
├──────────────────────┤    └──────────────────┘  ├──────────────┤
│ • Chat Completion    │                           │ • Caching    │
│ • Structured Output  │                           │ • Job Queue  │
│ • Multi-Model       │                           │ • Locking    │
└──────────┬───────────┘                           └──────────────┘
           │                                               │
           ▼                                               ▼
┌──────────────────────┐                     ┌────────────────────┐
│     OpenRouter       │                     │ Background Worker  │
│  (LLM Provider)      │                     │ (Job Processor)    │
└──────────────────────┘                     └────────────────────┘
```

## Key Design Decisions

### 1. Confidence Level Approach
**Decision**: Hybrid approach with qualitative labels + explicit rationale

**Implementation**:
- `confidence_level`: "high" | "medium" | "low" (qualitative)
- `rationale`: Explicit explanation for each confidence assessment

**Why this approach**:
- **Human-readable**: Engineering leaders can quickly scan confidence levels
- **Explainable**: The rationale field provides transparency into why the AI assigned that confidence
- **Actionable**: Teams can focus review efforts on low-confidence items
- **Reduces hallucination impact**: By forcing the LLM to explain its reasoning

Example:
```json
{
  "confidence_level": "high",
  "rationale": "Clear refactoring visible in UserService.cs lines 45-67 with explicit null checks added"
}
```

### 2. JSON Schema Design
**Decision**: Custom schema optimized for analysis workflow (not GitHub mirror)

**Our Schema**:
```json
{
  "pull_request_data": {
    "number": 123,
    "title": "...",
    "commits": [...],
    "changed_files": [
      {
        "filename": "...",
        "patch": "full diff content embedded"  // ← Key decision
      }
    ]
  }
}
```

**Why embedded diffs**:
- **Single source of truth**: All data needed for analysis in one place
- **Offline analysis**: Can analyze without additional API calls
- **Consistency**: Ensures analysis is based on snapshot at fetch time
- **Performance**: Avoids N+1 API calls during analysis
- **Caching**: Entire PR data can be cached as single unit

**Trade-offs accepted**:
- Larger JSON payloads (acceptable for local/small-scale deployment)
- Redundant data if analyzing same PR multiple times (mitigated by caching)

### 3. Analysis Depth
**Decision**: Deep structural analysis, not just commit message summarization

**What we analyze**:
1. **Behavioral changes**: What the code actually does differently
2. **Structural changes**: How the architecture/organization changed
3. **Test coverage signals**: Whether tests were added/modified
4. **Risk assessment**: Security, performance, maintainability concerns
5. **Claimed vs Actual**: Discrepancies between PR description and implementation

### 4. LLM Integration
**Decision**: Microsoft Semantic Kernel with OpenRouter

**Why Semantic Kernel**:
- **Production-ready**: Enterprise-grade framework from Microsoft
- **Type safety**: Strongly typed in C# with compile-time checks
- **Extensibility**: Plugin architecture for future enhancements
- **Multi-model support**: Works with any OpenAI-compatible API
- **Built-in features**: Chat history, token management, retry logic

**Why OpenRouter**:
- Model flexibility (Claude, GPT-4, Gemini, etc.)
- Cost optimization per use case
- Fallback options if one provider fails
- Single API interface for multiple providers

**Implementation Details**:
```csharp
// Semantic Kernel setup
builder.AddOpenAIChatCompletion(
    modelId: "google/gemini-2.0-flash-exp:free",
    apiKey: apiKey,
    endpoint: new Uri("https://openrouter.ai/api/v1")
);

// Structured output with JSON mode
var executionSettings = new OpenAIPromptExecutionSettings
{
    Temperature = 0.2,
    MaxTokens = 4096,
    ResponseFormat = "json_object"
};
```

**Prompt Engineering**:
- System prompt with anti-hallucination rules
- Few-shot learning example
- Evidence-based requirements (must cite line numbers)
- Confidence levels with rationale

### 5. Synchronous vs Asynchronous
**Decision**: Provide both options

**Endpoints**:
- `/api/analyze` - Synchronous (required by spec, good for small PRs)
- `/api/v2/analyze-async` - Asynchronous (better for large PRs, webhooks)

**Why both**:
- Small PRs (< 10 files): Synchronous is simpler, faster UX
- Large PRs (> 10 files): Async prevents timeouts, better for CI/CD integration
- Flexibility for different use cases

### 6. Caching Strategy
**Decision**: Redis with TTL-based invalidation

**Cache levels**:
1. **PR Data**: 1 hour (GitHub data)
2. **Analysis Results**: 24 hours (LLM responses)
3. **Jobs**: 7 days (async job history)

**Why Redis**:
- Fast access for repeated analyses
- Distributed cache if scaling needed
- Built-in TTL support
- Supports both structured and unstructured data

### 7. Observability & Validation
**Decision**: Built-in validation pipeline with structured logging

**Validation Pipeline**:
```csharp
private ValidationResult ValidateResponse(result, actualFiles, pr)
{
    // 1. Check for hallucinated files
    // 2. Verify evidence exists in diffs
    // 3. Return validation issues
}
```

**What we track**:
- LLM token usage and costs (via Semantic Kernel)
- Analysis latency
- Hallucination detection results
- Cache hit rates
- API endpoint performance

**Logging Strategy**:
- Structured logging with ILogger
- Request/response correlation
- Error tracking with stack traces
- Performance metrics

**Why this approach**:
- No external dependencies for observability
- Simple, maintainable validation logic
- Clear audit trail for debugging
- Cost monitoring built into LLM service

## Trade-offs and Limitations

### Accepted Trade-offs:
1. **Complexity over simplicity**: Added caching, queues, observability for production readiness
2. **Cost over speed**: Using larger LLMs for better accuracy
3. **Embedded data over references**: Larger payloads for simpler architecture

### Known Limitations:
1. **Large diffs**: May hit token limits on massive PRs (>100 files)
2. **Binary files**: Cannot analyze images, compiled code, etc.
3. **Context windows**: Very large PRs may need chunking strategies
4. **Rate limits**: Dependent on GitHub API and LLM provider limits

## Future Improvements

If this were to become a real product:

1. **Incremental analysis**: Only analyze changed files since last review
2. **Team learning**: Fine-tune on team's historical PR patterns
3. **IDE integration**: Surface insights directly in code review tools
4. **Metrics dashboard**: Track team patterns over time
5. **Custom rules**: Let teams define what they consider "risky"
6. **Multi-repo support**: Analyze PRs across microservices
7. **Real-time collaboration**: Multiple reviewers see same analysis

## Conclusion

The design prioritizes **accuracy and explainability** over speed, with the hypothesis that engineering leaders value **trustworthy insights** more than instant results. The hybrid confidence approach and embedded diff strategy optimize for the specific use case of understanding "what actually changed" in a codebase.