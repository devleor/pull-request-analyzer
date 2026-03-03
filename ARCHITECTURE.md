# Architecture Documentation

## System Overview

The Pull Request Analyzer is a production-ready microservice that analyzes GitHub pull requests using Microsoft Semantic Kernel and AI to understand what was **actually implemented** versus what was **claimed** in PR descriptions.

## High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         External Clients                         │
│            (Web Apps, CI/CD Systems, Webhook Receivers)         │
└─────────────────────────┬───────────────────────────────────────┘
                          │ HTTPS
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│                    ASP.NET Core 8.0 Web API                     │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ Controllers                                                │ │
│  │ • AnalyzeController - Main analysis endpoint (sync/async)  │ │
│  │ • PullRequestController - GitHub data fetching             │ │
│  │ • AsyncAnalysisController - Job management                 │ │
│  └────────────────────────────────────────────────────────────┘ │
│                          │                                       │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ Background Worker (IHostedService)                         │ │
│  │ • RedisBackgroundWorker - Processes async jobs             │ │
│  │ • Consumes from Redis Streams                              │ │
│  │ • Sends webhook notifications                              │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────┬───────────────────────────────────────┘
                          │
        ┌─────────────────┼─────────────────┬─────────────────┐
        ▼                 ▼                 ▼                 ▼
┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────┐
│  Semantic    │  │   GitHub     │  │    Redis     │  │ OpenRouter│
│   Kernel     │  │   API        │  │              │  │   LLM    │
├──────────────┤  │  (Octokit)   │  ├──────────────┤  ├──────────┤
│ • Chat       │  └──────────────┘  │ • Cache      │  │ • Gemini │
│ • JSON Mode  │                     │ • Queue      │  │ • Claude │
│ • Validation │                     │ • Locks      │  │ • GPT-4  │
└──────────────┘                     └──────────────┘  └──────────┘
```

## Component Details

### 1. API Layer (Controllers)

```csharp
AnalyzeController
├── POST /api/analyze
│   ├── Synchronous mode (no webhook_url)
│   │   └── Returns analysis immediately
│   └── Asynchronous mode (with webhook_url)
│       └── Returns job_id, sends results to webhook

PullRequestController
├── GET /api/pull-requests/{owner}/{repo}/{number}
│   └── Fetches and normalizes PR data from GitHub
└── GET /api/pull-requests/{owner}/{repo}/{number}/commits
    └── Returns commit list for PR

AsyncAnalysisController
├── POST /api/v2/analyze-async
│   └── Always async, optional webhook
├── GET /api/v2/jobs/{jobId}
│   └── Check job status and results
└── GET /api/v2/jobs
    └── List all jobs with status
```

### 2. Service Layer

#### Core Analysis Service
```csharp
SemanticKernelAnalysisService : IAnalysisService
├── Semantic Kernel Integration
│   ├── OpenAI-compatible connector
│   ├── Chat completion with history
│   └── Structured JSON output mode
├── Prompt Engineering
│   ├── System prompt with anti-hallucination rules
│   ├── Few-shot learning example
│   └── Evidence-based requirements
└── Validation Pipeline
    ├── File existence checking
    ├── Evidence verification in diffs
    └── Confidence scoring with rationale
```

#### Supporting Services
```csharp
GitHubIngestService : IGitHubService
├── Octokit client wrapper
└── PR data fetching and normalization

RedisCacheService
├── Analysis result caching (24h TTL)
├── PR data caching (1h TTL)
└── Job status tracking (7d TTL)

RedisJobQueue
├── Redis Streams for job queue
├── Message acknowledgment
└── Dead letter queue handling

RedisBackgroundWorker : BackgroundService
├── Continuous job processing
├── Distributed locking (RedLock)
└── Webhook delivery on completion

WebhookService
└── HTTP POST to callback URLs
```

## Data Flow Patterns

### Synchronous Analysis Flow
```
1. Client → POST /api/analyze
2. Controller → Check Redis Cache
3. [Cache Miss] → SemanticKernelAnalysisService
4. Service → Build prompt with PR data
5. Semantic Kernel → OpenRouter API (LLM)
6. LLM Response → Validation Pipeline
7. Validated Result → Cache (Redis)
8. Controller → Return to Client
```

### Asynchronous Analysis Flow
```
1. Client → POST /api/analyze (with webhook_url)
2. Controller → Create Job → Redis Stream
3. Controller → Return job_id to Client
4. BackgroundWorker → Poll Redis Stream
5. Worker → Acquire RedLock for PR
6. Worker → Check Cache
7. [Cache Miss] → SemanticKernelAnalysisService
8. Service → LLM Analysis (same as sync)
9. Worker → Cache Result
10. Worker → Update Job Status
11. Worker → POST to webhook_url
```

## Anti-Hallucination Architecture

### Multi-Layer Validation
```
┌─────────────────────────────────────────────────┐
│           LLM Response (Raw JSON)                │
└─────────────────────┬───────────────────────────┘
                      ▼
┌─────────────────────────────────────────────────┐
│         1. JSON Structure Validation             │
│         (Parse and validate schema)              │
└─────────────────────┬───────────────────────────┘
                      ▼
┌─────────────────────────────────────────────────┐
│         2. File Reference Validation             │
│         (Check all files exist in PR)            │
└─────────────────────┬───────────────────────────┘
                      ▼
┌─────────────────────────────────────────────────┐
│         3. Evidence Validation                   │
│         (Verify quotes exist in diffs)           │
└─────────────────────┬───────────────────────────┘
                      ▼
┌─────────────────────────────────────────────────┐
│         4. Confidence Assessment                 │
│         (HIGH: evidence, MEDIUM: inferred, LOW)  │
└─────────────────────┬───────────────────────────┘
                      ▼
┌─────────────────────────────────────────────────┐
│           Validated Analysis Result              │
└─────────────────────────────────────────────────┘
```

### Grounding Techniques

1. **Mandatory Evidence Citations**
   - Every claim must quote exact text from diffs
   - Line numbers and file paths required

2. **Confidence Levels with Rationale**
   ```json
   {
     "confidence_level": "high",
     "evidence": "@@ -45,8 +45,11 @@ user.Profile = profile",
     "rationale": "Direct evidence of null check addition in diff"
   }
   ```

3. **Few-Shot Learning**
   - Example analysis included in system prompt
   - Shows proper evidence citation format

4. **Structured Output Enforcement**
   - JSON schema validation
   - Required fields enforcement
   - Type checking

## Redis Data Model

### Key Patterns
| Pattern | Type | TTL | Purpose |
|---------|------|-----|---------|
| `cache:pr:{owner}:{repo}:{number}` | String (JSON) | 1 hour | GitHub PR data |
| `cache:analysis:{owner}:{repo}:{number}` | String (JSON) | 24 hours | Analysis results |
| `job:{jobId}` | String (JSON) | 7 days | Job status/results |
| `queue:analyze` | Stream | - | Job queue |
| `lock:pr:{owner}:{repo}:{number}` | String | 60s | Distributed lock |

### Job Status Record
```csharp
public record JobStatusRecord(
    string JobId,
    string Status,        // queued, processing, completed, failed
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int PrNumber,
    AnalysisResult? Result,
    string? ErrorMessage
);
```

## Technology Stack

### Core
- **.NET 8.0** - Runtime platform
- **ASP.NET Core 8.0** - Web framework
- **C# 12** - Programming language

### AI/ML
- **Microsoft.SemanticKernel 1.29.0** - AI orchestration
- **OpenRouter API** - LLM provider aggregator
- **Supported Models**:
  - Google Gemini 2.0 Flash (default)
  - Anthropic Claude 3.5 Sonnet
  - OpenAI GPT-4o
  - Meta Llama 3

### Infrastructure
- **Redis 7.0** - Cache, queue, distributed locking
- **Docker** - Containerization
- **Docker Compose** - Local orchestration

### Libraries
- **Octokit 14.0.0** - GitHub API client
- **StackExchange.Redis 2.11.8** - Redis client
- **RedLock.net 2.3.2** - Distributed locking
- **Swashbuckle 6.6.2** - OpenAPI/Swagger

## Configuration

### Environment Variables
```bash
# Required
GITHUB_TOKEN=ghp_...                    # GitHub API access
OPENROUTER_API_KEY=sk-or-v1-...        # LLM provider

# Optional
OPENROUTER_MODEL=google/gemini-2.0-flash-exp:free
REDIS_URL=localhost:6379
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://+:5000
```

### Docker Compose Services
```yaml
services:
  api:         # ASP.NET Core application
  redis:       # Redis server
  redis-ui:    # Redis Commander (dev only)
  phoenix:     # Arize Phoenix (optional observability)
```

## Performance Characteristics

### Response Times
- **Cache Hit**: < 100ms
- **Small PR (< 10 files)**: 5-15 seconds
- **Medium PR (10-50 files)**: 15-45 seconds
- **Large PR (> 50 files)**: Use async mode

### Throughput
- **Concurrent Analysis**: Limited by LLM rate limits
- **Cache Capacity**: ~10,000 analyses (configurable)
- **Job Queue**: Unlimited (Redis Streams)

### Resource Usage
- **Memory**: ~200MB baseline + cache
- **CPU**: Low, except during JSON parsing
- **Network**: Minimal, mostly to LLM provider

## Security Considerations

### API Security
- GitHub token for API authentication
- OpenRouter API key protection
- Environment variable management
- No persistent storage of code

### Data Privacy
- Ephemeral cache with TTL
- No database persistence
- Webhook URL validation
- HTTPS only in production

## Monitoring & Observability

### Logging
- Structured logging with ILogger
- Request correlation IDs
- Performance metrics
- Error tracking

### Health Checks
```http
GET /health
{
  "status": "healthy",
  "redis": {
    "status": "connected",
    "latency_ms": 0.23
  }
}
```

### Metrics Tracked
- LLM token usage and costs
- Cache hit rates
- Analysis duration
- Validation failures
- Job queue depth

## Deployment

### Local Development
```bash
make dev  # Starts everything
```

### Production Docker
```bash
docker build -t pr-analyzer .
docker run -e GITHUB_TOKEN=... -e OPENROUTER_API_KEY=... pr-analyzer
```

### Kubernetes (Future)
- Horizontal pod autoscaling
- Redis cluster for HA
- Ingress with TLS termination
- ConfigMaps for configuration

## Future Enhancements

### Planned Features
- Batch analysis for multiple PRs
- Incremental analysis (only new commits)
- Custom validation rules per repository
- Integration with CI/CD pipelines
- Real-time analysis via webhooks

### Architectural Evolution
- GraphQL API alongside REST
- Event sourcing for audit trail
- CQRS for read/write separation
- Multi-region deployment
- WebSocket for real-time updates

## Decision Log

| Decision | Rationale |
|----------|-----------|
| Semantic Kernel over raw HTTP | Production-ready, type-safe, extensible |
| Redis for cache and queue | Simple, fast, reduces dependencies |
| Embedded diffs in JSON | Avoids extra API calls during analysis |
| Hybrid confidence approach | Balance between transparency and simplicity |
| Single container deployment | Simplifies operations for MVP |

## Conclusion

The architecture prioritizes:
- **Accuracy** through multi-layer validation
- **Reliability** through caching and retries
- **Scalability** through async processing
- **Maintainability** through clean separation of concerns
- **Extensibility** through interface-based design

The use of Microsoft Semantic Kernel provides enterprise-grade AI integration while maintaining the flexibility to adapt to changing requirements and LLM providers.