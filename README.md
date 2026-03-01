# Pull Request Analyzer

An end-to-end prototype that ingests GitHub pull request data, analyzes what was actually implemented using an LLM workflow, and exposes the functionality via a local API. Built as a take-home assignment for a Founding Engineer role at Mesmer.

## Overview

The system is built with **C# and ASP.NET Core 8** and performs the following:

1. **Ingests PR data** — fetches metadata, commits, and file diffs from the GitHub API via Octokit.
2. **Analyzes with LLM** — sends structured prompts to an LLM (via OpenRouter BYOK) to produce a structured analysis grounded in the actual diffs.
3. **Processes asynchronously** — uses Redis Streams as a job queue and an `IHostedService` background worker to process analyses without blocking the API.
4. **Exposes via API** — provides local endpoints to fetch PR data, trigger analysis, and check job status.

## Architecture

| Responsibility | Solution |
|---|---|
| Job queue | Redis Streams (`RedisJobQueue`) |
| Cache | Redis — PR data, analyses, job status with TTL |
| Distributed lock | RedLock.net — prevents duplicate LLM calls |
| Background worker | `RedisBackgroundWorker` (`IHostedService`) |
| LLM provider | OpenRouter BYOK |

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full system diagram and [DESIGN_NOTES.md](DESIGN_NOTES.md) for architectural decisions and tradeoffs.

## Project Structure

```
PullRequestAnalyzer/
├── Controllers/                   # HTTP endpoints
│   ├── PullRequestController.cs   # GET /pull-requests/:owner/:repo/:number
│   └── AsyncAnalysisController.cs # POST /analyze, GET /jobs/:id
├── Services/
│   ├── GitHubIngestService.cs     # GitHub API client (Octokit)
│   ├── LLMAnalysisService.cs      # LLM workflow + prompt engineering
│   ├── DiffChunkingService.cs     # Diff truncation for large PRs
│   ├── RedisCacheService.cs       # Cache + job status store
│   ├── RedisJobQueue.cs           # Redis Streams queue
│   ├── RedLockService.cs          # Distributed locking
│   ├── RedisBackgroundWorker.cs   # IHostedService consumer
│   └── WebhookService.cs          # Webhook notifications
├── Models/
│   ├── PullRequestData.cs         # PR + commit + diff schema
│   ├── AnalysisResult.cs          # LLM output schema
│   ├── PrIdentifier.cs            # Value object: owner/repo/number
│   └── Result.cs                  # Result<T> error pattern
├── Messages/
│   └── AnalyzePullRequestCommand.cs
├── scripts/
│   └── generate_pr_json.py        # Script to generate real PR JSON from GitHub API
├── docs/
│   ├── flow.mmd                   # Mermaid diagram source
│   └── flow.png                   # Rendered architecture diagram
├── example_pr_data.json           # Example PR JSON (mock)
├── docker-compose.yml             # Redis + Redis Commander
├── Makefile                       # Automation
└── .env.example                   # Environment variable template
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://docs.docker.com/get-docker/) (for Redis)
- A GitHub Personal Access Token (`repo` scope)
- An OpenRouter API key ([openrouter.ai/keys](https://openrouter.ai/keys))

## Setup

**1. Clone the repository:**

```bash
git clone https://github.com/devleor/pull-request-analyzer.git
cd pull-request-analyzer
```

**2. Configure environment variables:**

```bash
cp .env.example .env
```

Edit `.env` and fill in your values:

| Variable | Description | Where to get |
|---|---|---|
| `GITHUB_TOKEN` | GitHub Personal Access Token | [github.com/settings/tokens](https://github.com/settings/tokens) — `repo` scope |
| `OPENROUTER_API_KEY` | OpenRouter API key | [openrouter.ai/keys](https://openrouter.ai/keys) |
| `OPENROUTER_MODEL` | LLM model to use | e.g. `anthropic/claude-3.5-sonnet`, `openai/gpt-4o` |
| `REDIS_URL` | Redis connection string | `localhost:6379` (default via Docker) |

**3. Start everything:**

```bash
make dev
```

This starts Docker (Redis + Redis Commander), waits for Redis to be healthy, builds the project, and starts the API with the background worker.

## Running the API

After `make dev`, the following services are available:

| Service | URL |
|---|---|
| API + Swagger UI | http://localhost:5000/swagger |
| Health check | http://localhost:5000/health |
| Redis Commander UI | http://localhost:8081 |

## Generating a Real PR JSON from MindsDB

Use the provided Python script to generate a normalized PR JSON from any GitHub pull request:

```bash
export GITHUB_TOKEN=your_token_here
python3 scripts/generate_pr_json.py mindsdb mindsdb 9876
```

This produces a file `pr_mindsdb_mindsdb_9876.json` in the current directory. The script uses only the Python standard library — no dependencies to install.

The `example_pr_data.json` in the repository is a realistic mock of a MindsDB PR. Replace it with a real one using the script above.

## API Endpoints

### GET /api/pull-requests/:owner/:repo/:number

Returns the normalized PR JSON, fetched from GitHub and cached in Redis (TTL: 10 minutes).

```bash
curl http://localhost:5000/api/pull-requests/mindsdb/mindsdb/9876
```

### GET /api/pull-requests/:owner/:repo/:number/commits

Returns the list of commits for the PR.

```bash
curl http://localhost:5000/api/pull-requests/mindsdb/mindsdb/9876/commits
```

### POST /api/v2/analyze-async

Accepts PR JSON and returns a job ID immediately (202 Accepted). The analysis runs in the background.

```bash
curl -X POST http://localhost:5000/api/v2/analyze-async \
  -H "Content-Type: application/json" \
  -d '{
    "pull_request_data": '"$(cat example_pr_data.json)"',
    "webhook_url": "https://webhook.site/your-id"
  }'
```

**Response (202 Accepted):**

```json
{
  "job_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "status": "queued",
  "created_at": "2026-03-01T12:00:00Z",
  "status_check_url": "/api/v2/jobs/a1b2c3d4-e5f6-7890-abcd-ef1234567890"
}
```

### GET /api/v2/jobs/:jobId

Returns the current status and result of an analysis job.

```bash
curl http://localhost:5000/api/v2/jobs/a1b2c3d4-e5f6-7890-abcd-ef1234567890
```

**Response (200 OK, completed):**

```json
{
  "job_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "status": "completed",
  "created_at": "2026-03-01T12:00:00Z",
  "started_at": "2026-03-01T12:00:01Z",
  "completed_at": "2026-03-01T12:01:30Z",
  "pr_number": 9876,
  "analysis_result": {
    "executive_summary": [
      "Adds a new distributed query optimization framework",
      "Introduces cost-based planning via QueryOptimizer class",
      "Integrates with the existing query pipeline"
    ],
    "change_units": [{
      "type": "feature",
      "title": "QueryOptimizer class",
      "description": "New cost-based query optimizer with transformation pipeline",
      "affected_files": ["mindsdb/query/optimizer/query_optimizer.py"],
      "inferred_intent": "Improve query performance for large-scale distributed queries",
      "confidence_level": "high",
      "test_coverage_signal": "high"
    }],
    "risks_and_concerns": [
      "No rollback strategy documented if optimizer produces suboptimal plans"
    ],
    "claimed_vs_actual": {
      "alignment_assessment": "aligned",
      "discrepancies": []
    }
  }
}
```

## Stopping

```bash
make stop
```

## All Makefile Commands

```bash
make dev          # Start everything (recommended)
make stop         # Stop everything
make restart      # Restart everything
make build        # Build only
make test         # Run tests
make docker-logs  # View Redis logs
make health       # Check service health
make help         # List all commands
```
