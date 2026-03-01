# Design Notes: Pull Request Analyzer

This document covers architectural decisions, the LLM workflow, diff handling strategy, tradeoffs, known limitations, and planned improvements.

## 1. Architectural Decisions

The system is built with **C# and ASP.NET Core 8** using a service-oriented architecture where both the HTTP API and the background worker run in the same process. This choice was deliberate for a local prototype: it eliminates the need for inter-process communication, simplifies deployment to a single `make dev` command, and keeps the codebase small and navigable.

**Technology choices:**

| Concern | Choice | Rationale |
|---|---|---|
| API framework | ASP.NET Core 8 | High performance, minimal boilerplate, native DI |
| GitHub client | Octokit | Official .NET library, handles pagination and rate limits |
| JSON serialization | `System.Text.Json` | Native to ASP.NET Core, no extra dependency |
| Job queue | Redis Streams | Persistent, consumer groups, built-in DLQ pattern |
| Cache | Redis (StackExchange.Redis) | Unified infra with queue, TTL-based expiry |
| Distributed lock | RedLock.net | Prevents duplicate LLM calls for the same PR |
| LLM provider | OpenRouter (BYOK) | Model-agnostic, supports Claude, GPT-4, Gemini |

**Design patterns applied:**

- **Strategy** (`IAnalysisService`, `IGitHubService`): Swap LLM provider or GitHub data source without touching consumers.
- **Value Object** (`PrIdentifier`): Encapsulates `owner/repo/number` tuple used throughout the codebase.
- **Result Pattern** (`Result<T>`): Typed error handling without exception-based control flow in controllers.
- **IHttpClientFactory**: Correct `HttpClient` lifecycle, avoids socket exhaustion.

## 2. LLM Workflow Structure

The analysis pipeline runs inside `RedisBackgroundWorker` and follows these steps:

**Step 1 — Data preparation.** `GitHubIngestService` fetches the PR metadata, all commits, and all changed files with their diffs from the GitHub API via Octokit. The result is a normalized `PullRequestData` object.

**Step 2 — Cache check.** Before calling the LLM, `RedisCacheService` checks for a cached `AnalysisResult` for the same PR. If found and not expired (TTL: 1 hour), the cached result is returned immediately. This avoids redundant LLM calls for repeated requests on the same PR.

**Step 3 — Prompt construction.** `LLMAnalysisService.BuildPrompt()` assembles a structured prompt that includes: PR title, description, author, all commit messages with their SHAs, and each changed file with its filename, status, and diff patch. The prompt instructs the LLM to return a specific JSON schema.

**Step 4 — LLM call.** The prompt is sent to OpenRouter using the configured model (`OPENROUTER_MODEL`, defaults to `anthropic/claude-3.5-sonnet`). The request uses `temperature: 0.3` for deterministic, structured output and `max_tokens: 4000`.

**Step 5 — Response parsing.** `ParseResponse()` extracts the JSON from the LLM response (handling both raw JSON and markdown code blocks), then maps it to a typed `AnalysisResult` object.

**Step 6 — Result storage and notification.** The result is cached in Redis, the job status is updated to `completed`, and a webhook POST is sent to the client-provided URL if one was given.

**Output schema:**

```json
{
  "executive_summary": ["2–6 bullet points summarizing the PR"],
  "change_units": [{
    "type": "feature|bugfix|refactor|test|documentation|performance",
    "title": "Short title",
    "description": "What changed",
    "affected_files": ["path/to/file.py"],
    "inferred_intent": "Why it likely changed",
    "confidence_level": "high|medium|low",
    "test_coverage_signal": "high|medium|low|none"
  }],
  "risks_and_concerns": ["Notable risks or concerns"],
  "claimed_vs_actual": {
    "alignment_assessment": "aligned|partially_aligned|misaligned",
    "discrepancies": ["What the PR claims vs what the diffs show"]
  }
}
```

## 3. Handling Large Diffs

Large PRs (like the MindsDB example commit with 29 files) present a context window challenge. The system handles this through `DiffChunkingService`:

**Patch truncation.** Each file's diff patch is truncated to a configurable maximum (default: 2,000 characters) using `TruncatePatch()`. This preserves the beginning of the diff — where the most structurally significant changes typically appear — while preventing context overflow.

**File prioritization.** Files are included in the prompt in the order returned by the GitHub API, which generally prioritizes modified source files over generated or lock files. A future improvement would explicitly filter out `package-lock.json`, `*.min.js`, and similar noise files before sending to the LLM.

**Known limitation.** Truncation means the LLM may not see the full context of very large files. For PRs with 50+ files or diffs exceeding 100k characters, a multi-pass approach (analyze files in batches, then synthesize) would produce better results.

## 4. Tradeoffs

**Unified process vs. separate worker.** Running the API and background worker in the same process simplifies local development and deployment. The tradeoff is that a crash in the worker could affect the API. For production, the `RedisBackgroundWorker` should be extracted into a separate Worker Service container.

**Redis Streams vs. a dedicated message broker.** Redis Streams provides persistent queuing, consumer groups, and a dead-letter queue pattern without adding a second infrastructure dependency. RabbitMQ or Azure Service Bus would be justified when multiple independent services need to communicate, or when advanced routing, priority queues, or cross-language consumers are required.

**File-based example vs. real MindsDB PR.** The `example_pr_data.json` is a realistic mock. A real MindsDB PR JSON can be generated using `scripts/generate_pr_json.py` once a valid `GITHUB_TOKEN` is provided. The mock was created to allow testing without API credentials.

**Fixed confidence score.** `AnalysisResult.ConfidenceScore` is hardcoded to `0.85`. A proper implementation would derive this from the LLM's `confidence_level` fields across all change units.

## 5. Known Limitations

- The `example_pr_data.json` is a mock, not a real MindsDB PR. Use `GET /api/pull-requests/mindsdb/mindsdb/:number` with a valid `GITHUB_TOKEN` to fetch and save a real one.
- Diff truncation at 2,000 characters per file may miss important context in large files.
- No retry with exponential backoff on LLM API failures (the DLQ captures failed jobs, but no automatic retry is implemented).
- `RedisCacheService.GetAllJobIdsAsync()` uses a Redis `KEYS` scan, which is not suitable for production at scale. A Redis Set should track job IDs instead.
- No authentication on API endpoints.

## 6. What Would Be Improved Next

**Short term:**
- Generate and commit a real MindsDB PR JSON file using the provided script.
- Add retry with exponential backoff for LLM calls using Polly.
- Replace `KEYS` scan with a Redis Set for job ID tracking.
- Add diff noise filtering (skip `package-lock.json`, `*.min.js`, migration files).

**Medium term:**
- Multi-pass analysis for large PRs: analyze files in batches, then synthesize a final summary.
- Streaming LLM responses to reduce perceived latency on large PRs.
- Structured logging with OpenTelemetry for observability.

**Long term:**
- Extract the worker into a separate container for independent scaling.
- Add a lightweight frontend (Blazor or React) to visualize analysis results.
- Store analysis history in a relational database for querying and trending.
