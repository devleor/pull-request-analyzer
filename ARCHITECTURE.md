# Architecture: Pull Request Analyzer

The Pull Request Analyzer uses a **unified service architecture** where both the API and the background worker run in the same process, communicating through **Redis Streams**.

## High-Level Diagram

```
┌─────────────────────────────────────────────────────────────┐
│  Single Container Instance                                  │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  ASP.NET Core API                                    │  │
│  │  - Receives HTTP requests                            │  │
│  │  - Publishes jobs to Redis Stream                    │  │
│  │  - Returns 202 Accepted + job_id immediately         │  │
│  └──────────────────────────────────────────────────────┘  │
│                          │                                  │
│                          ▼                                  │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  RedisBackgroundWorker (IHostedService)              │  │
│  │  - Consumes jobs from Redis Stream                   │  │
│  │  - Acquires RedLock before processing                │  │
│  │  - Calls LLMAnalysisService                          │  │
│  │  - Updates job status in Redis                       │  │
│  │  - Sends webhook on completion                       │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                             │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
                 ┌─────────────────┐
                 │     Redis 7     │
                 │                 │
                 │  Streams        │  ← Job queue
                 │  Strings (TTL)  │  ← Cache + Job status
                 │  Keys (TTL)     │  ← RedLock
                 └─────────────────┘
```

## Redis Key Schema

| Key Pattern | Type | TTL | Purpose |
|---|---|---|---|
| `queue:analyze` | Stream | — | Job queue |
| `queue:analyze:dlq` | Stream | — | Dead-letter queue (failed jobs) |
| `cache:pr:{owner}/{repo}/{n}` | String (JSON) | 10 min | GitHub PR data cache |
| `cache:analysis:{owner}/{repo}/{n}` | String (JSON) | 1 hour | LLM analysis cache |
| `job:{jobId}` | String (JSON) | 24 hours | Job status and result |
| `lock:analyze:{owner}/{repo}/{n}` | String | 60 sec | RedLock — prevents duplicate processing |

## Async Job Flow

```
Client                   API                  Redis                  Worker
  │                       │                     │                       │
  │  POST /analyze-async  │                     │                       │
  │──────────────────────▶│                     │                       │
  │                       │  XADD queue:analyze │                       │
  │                       │────────────────────▶│                       │
  │                       │  SET job:{id}       │                       │
  │                       │────────────────────▶│                       │
  │  202 + job_id         │                     │                       │
  │◀──────────────────────│                     │                       │
  │                       │                     │  XREADGROUP           │
  │                       │                     │◀──────────────────────│
  │                       │                     │  SETNX lock:analyze:* │
  │                       │                     │◀──────────────────────│
  │                       │                     │  GET cache:analysis:* │
  │                       │                     │◀──────────────────────│
  │                       │                     │  SET job:{id}         │
  │                       │                     │◀──────────────────────│
  │                       │                     │  XACK queue:analyze   │
  │                       │                     │◀──────────────────────│
  │  POST webhook         │                     │                       │
  │◀──────────────────────────────────────────────────────────────────│
  │                       │                     │                       │
  │  GET /jobs/{id}       │                     │                       │
  │──────────────────────▶│                     │                       │
  │                       │  GET job:{id}       │                       │
  │                       │────────────────────▶│                       │
  │  200 + result         │                     │                       │
  │◀──────────────────────│                     │                       │
```

## Design Patterns Applied

| Pattern | Where | Benefit |
|---|---|---|
| **Strategy** | `IAnalysisService`, `IGitHubService` | Swap LLM provider or data source without changing consumers |
| **Value Object** | `PrIdentifier(owner, repo, number)` | Eliminates repetition of 3 parameters across the codebase |
| **Result Pattern** | `Result<T>` | Typed errors without exception-based control flow |
| **Factory Method** | `IHttpClientFactory` | Correct `HttpClient` lifecycle management |
| **Record Types** | All DTOs as `sealed record` | Immutability, structural equality, less boilerplate |

## Dependencies

```xml
<PackageReference Include="Microsoft.AspNetCore.OpenApi" />
<PackageReference Include="Octokit" />
<PackageReference Include="RedLock.net" />
<PackageReference Include="StackExchange.Redis" />
<PackageReference Include="Swashbuckle.AspNetCore" />
```

## Quick Start

```bash
make dev
```

This single command starts Docker (Redis + Redis Commander), waits for Redis to be healthy, builds the project, and starts the API with the background worker.

| Service | URL |
|---|---|
| API + Swagger | http://localhost:5000/swagger |
| Health Check | http://localhost:5000/health |
| Redis Commander UI | http://localhost:8081 |

## Future Enhancements

When the system grows to multiple independent services, the `RedisBackgroundWorker` can be extracted into a separate Worker Service project and deployed as its own container, scaling independently from the API. At that point, a dedicated message broker such as RabbitMQ or Azure Service Bus would become justified.
