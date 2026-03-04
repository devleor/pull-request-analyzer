# Pull Request Analyzer 🚀

**Production-Ready AI-Powered PR Analysis System**

A take-home assignment demonstrating enterprise-grade implementation of an LLM-powered GitHub pull request analyzer with full observability, anti-hallucination measures, and production features.

## 🎯 Core Value Proposition

Provides structured, evidence-based analysis of pull request changes by:
- Analyzing actual code diffs with anti-hallucination validation
- Tracking LLM costs, latency, and token usage via Langfuse
- Supporting both synchronous and asynchronous analysis modes
- Implementing production-grade rate limiting and error handling

## 🏗️ Architecture

```mermaid
graph TB
    Client[Client Application]
    API[ASP.NET Core 8.0 API]
    SK[Microsoft Semantic Kernel]
    LLM[OpenRouter Multi-Model]
    GitHub[GitHub API/Octokit]
    Redis[(Redis Cache + Queue)]
    Worker[Background Service]
    Langfuse[Langfuse Observability]
    OTEL[OpenTelemetry]

    Client -->|REST API| API
    API --> SK
    SK --> LLM
    SK --> OTEL
    OTEL -->|Traces| Langfuse
    API --> GitHub
    API --> Redis
    API -->|Async Jobs| Redis
    Redis --> Worker
    Worker --> SK
    Worker -->|Webhook| Client
```

## 🚀 Quick Start

### Prerequisites
- Docker & Docker Compose
- .NET 8.0 SDK
- GitHub Personal Access Token
- OpenRouter API Key
- Langfuse Account (for observability)

### Environment Setup

Create a `.env` file:
```bash
# Core Services
GITHUB_TOKEN=your_github_token
OPENROUTER_API_KEY=your_openrouter_key
OPENROUTER_MODEL=liquid/lfm-2.5-1.2b-instruct:free
REDIS_URL=localhost:6379

# Langfuse Observability
LANGFUSE_PUBLIC_KEY=your_public_key
LANGFUSE_SECRET_KEY=your_secret_key
LANGFUSE_HOST=https://us.cloud.langfuse.com
LANGFUSE_RELEASE=production
LANGFUSE_TIMEOUT=10
LANGFUSE_SAMPLE_RATE=1.0

# Rate Limiting
RATE_LIMIT_REQUESTS_PER_MINUTE=30
RATE_LIMIT_WINDOW_SECONDS=60
```

### Running the Application

```bash
# Start all services (Redis, API, Worker)
make dev

# Or using Docker Compose directly
docker-compose up --build

# Health check
curl http://localhost:5000/health

# API Documentation
open http://localhost:5000/swagger
```

## 📋 API Endpoints

### Core Required Endpoints

| Endpoint | Method | Description | Example |
|----------|--------|-------------|---------|
| `/api/pull-requests/:owner/:repo/:number` | GET | Fetch normalized PR data | `GET /api/pull-requests/mindsdb/mindsdb/11944` |
| `/api/pull-requests/:owner/:repo/:number/commits` | GET | Get PR commits | `GET /api/pull-requests/mindsdb/mindsdb/11944/commits` |
| `/api/analyze` | POST | Analyze PR (sync/async) | See examples below |

### Request Examples

#### Synchronous Analysis (Small PRs)
```bash
curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d '{
    "pull_request_data": {
      "number": 11944,
      "owner": "mindsdb",
      "repo": "mindsdb"
    }
  }'
```

#### Asynchronous Analysis with Webhook (Large PRs)
```bash
curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d '{
    "pull_request_data": {
      "number": 11944,
      "owner": "mindsdb",
      "repo": "mindsdb"
    },
    "webhook_url": "https://your-webhook.com/callback"
  }'
```

## 🧠 Production Features

### 1. LLM Observability with Langfuse
- **Token Tracking**: Monitor usage per request
- **Latency Metrics**: Track response times (~3-15s typical)
- **Cost Analysis**: Calculate costs per analysis (~$0.008 per PR)
- **OpenTelemetry Integration**: Full distributed tracing

### 2. Anti-Hallucination Measures
```csharp
// Evidence-based validation
- Every claim must cite specific diff lines
- File existence verification
- Confidence levels with explicit rationale
- Missing file detection and warnings
```

### 3. Rate Limiting
- Sliding window algorithm (30 requests/minute default)
- Per-IP tracking with distributed Redis backing
- Configurable via environment variables

### 4. Caching Strategy
| Data Type | TTL | Purpose |
|-----------|-----|---------|
| PR Data | 1 hour | GitHub API responses |
| Commits | 1 hour | Commit history |
| System Prompts | Never expires | LLM system prompts |
| Analysis Results | Not cached | Always fresh analysis |

### 5. Error Handling & Resilience
- Structured logging with Serilog
- Correlation IDs for request tracing
- Graceful degradation on service failures
- Health checks with detailed status

## 🛠️ Technology Stack

### Core Technologies
- **Framework**: ASP.NET Core 8.0 (Clean Architecture)
- **AI Orchestration**: Microsoft Semantic Kernel 1.29.0
- **LLM Provider**: OpenRouter (multi-model support)
- **Observability**: Langfuse via OpenTelemetry
- **Cache & Queue**: Redis 7.4 with StackExchange.Redis
- **GitHub Integration**: Octokit.NET 14.0.0
- **Containerization**: Docker & Docker Compose

## 🧪 Testing

### Comprehensive Test Script
```bash
# Test MindsDB PR #11944 (PocketBase Handler)
./test-pr-11944.sh

# Test all endpoints
./test-all-endpoints.sh

# Test fresh analysis (no cache)
./test-fresh-analysis.sh
```

### Sample Output Structure
```json
{
  "pr_number": 11944,
  "pr_title": "Pocketbase handler",
  "analysis_timestamp": "2026-03-04T04:53:59Z",
  "confidence_score": 0.91,
  "executive_summary": [
    "Added PocketBase handler for MindsDB integration",
    "Enables SQL queries on PocketBase collections"
  ],
  "change_units": [
    {
      "type": "feature",
      "title": "PocketBase Integration",
      "description": "New handler for PocketBase REST API",
      "confidence_level": "high",
      "evidence": "diff shows new handler class",
      "affected_files": ["mindsdb/integrations/handlers/pocketbase/"]
    }
  ],
  "risks_and_concerns": [
    "Potential authentication issues if misconfigured",
    "Dependency on external PocketBase service"
  ],
  "claimed_vs_actual": {
    "alignment_assessment": "aligned",
    "discrepancies": []
  }
}
```

## 📁 Project Structure

```
pull-request-analyzer/
├── Controllers/
│   ├── AnalyzeController.cs         # Main analysis endpoint
│   └── PullRequestController.cs     # GitHub data fetching
├── Services/
│   ├── LlmAnalysisService.cs       # Semantic Kernel + LLM
│   ├── TelemetryService.cs         # OpenTelemetry/Langfuse
│   ├── GitHubIngestService.cs      # GitHub API client
│   ├── RedisCacheService.cs        # Caching layer
│   ├── JobQueueService.cs          # Async job queue
│   ├── AnalysisBackgroundService.cs # Background worker
│   ├── PromptTemplateService.cs    # Prompt management
│   └── DistributedLockService.cs   # Redis locking
├── Models/
│   ├── AnalysisResult.cs           # Analysis DTOs
│   └── PullRequestData.cs          # GitHub models
├── docker-compose.yml               # Container orchestration
├── Dockerfile                       # Multi-stage build
└── Program.cs                       # DI configuration
```

**Built as a production-ready take-home assignment demonstrating enterprise-grade AI integration with focus on observability, accuracy, and scalability.**
