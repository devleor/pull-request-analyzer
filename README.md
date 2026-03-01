# Pull Request Analyzer

> [!WARNING]
> **Security Warning:** The user-provided GitHub token was exposed in the chat history. It is crucial to revoke this token immediately in your GitHub settings to prevent unauthorized access. A new token should be generated and used for any further interactions.

This project is a take-home assignment for a Founding Engineer role at Mesmer. It is an end-to-end prototype that ingests GitHub pull request data, analyzes the implementation using an LLM-based workflow, and exposes the functionality via a local API.

## Overview

The system is built with **C# and ASP.NET Core 8** and performs the following actions:

1.  **Ingests Pull Request Data**: Fetches PR metadata, commits, and file diffs from the GitHub API.
2.  **LLM-Based Analysis**: Uses a Large Language Model (via OpenRouter) to analyze the code changes, infer intent, and identify potential risks.
3.  **Asynchronous Processing**: Uses **Redis Streams** as a job queue and a background worker (`IHostedService`) to process analyses without blocking the API.
4.  **Exposes API Endpoints**: Provides a local API to trigger analysis, check job status, and access PR data.

## Final Architecture

| Responsibility | Solution |
|---|---|
| **Fila de jobs** | Redis Streams (`RedisJobQueue`) |
| **Cache** | Redis — PR data, análises, job status com TTL |
| **Distributed lock** | RedLock.net — evita processamento duplicado |
| **Worker** | `RedisBackgroundWorker` (`IHostedService`) |
| **LLM** | OpenRouter BYOK |

## Project Structure

```
/PullRequestAnalyzer
├── Controllers/          # API endpoints (v1 e v2)
├── Messages/             # Comandos e eventos
├── Models/               # Schemas de dados
├── Services/             # Lógica de negócio + Worker
├── Program.cs            # Entry point
├── appsettings.json      # Configurações
├── docker-compose.yml    # Redis + Redis Commander
├── Makefile              # Automação
├── README.md             # Documentação principal
├── ARCHITECTURE.md       # Diagrama e explicação
├── DESIGN_NOTES.md       # Decisões de design
└── .env.example          # Template de variáveis de ambiente
```

## Setup and Dependencies

### Prerequisites

- **.NET 8 SDK**: [Download & Install .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Docker**: For running Redis.

### Installation

1.  **Clone the repository**:

    ```bash
    git clone https://github.com/devleor/pull-request-analyzer.git
    cd pull-request-analyzer
    ```

2.  **Restore dependencies**:

    ```bash
    make restore
    ```

## Environment Variables

Copy `.env.example` to `.env` and fill in your values:

```bash
cp .env.example .env
```

| Variável | Serviço | Onde obter |
|---|---|---|
| `GITHUB_TOKEN` | GitHub API | [github.com/settings/tokens](https://github.com/settings/tokens) |
| `OPENROUTER_API_KEY` | LLM via OpenRouter | [openrouter.ai/keys](https://openrouter.ai/keys) |
| `OPENROUTER_MODEL` | Modelo LLM | Ex: `anthropic/claude-3.5-sonnet` |
| `REDIS_URL` | Redis | `localhost:6379` (via Docker) |

## How to Run

Execute the following command from the root of the `PullRequestAnalyzer` directory:

```bash
make dev
```

This will:
1.  Start Docker (Redis + Redis Commander)
2.  Wait for Redis to be ready
3.  Build the project
4.  Start the API and background worker

### Services

| Serviço | URL | Credenciais |
|---|---|---|
| API | http://localhost:5000/swagger | — |
| Redis | localhost:6379 | — |
| Redis Commander UI | http://localhost:8081 | — |

## Example Request/Response

### POST /api/v2/analyze-async

This endpoint accepts the PR data and a webhook URL, and returns a job ID.

**Request Body**:

```json
{
  "pull_request_data": { ... },
  "webhook_url": "https://your-webhook-receiver.com/hook"
}
```

**Success Response (202 Accepted)**:

```json
{
  "job_id": "a1b2c3d4-e5f6-7890-1234-567890abcdef",
  "status_url": "http://localhost:5000/api/v2/jobs/a1b2c3d4-e5f6-7890-1234-567890abcdef"
}
```

### GET /api/v2/jobs/:jobId

This endpoint returns the status and result of an analysis job.

**Success Response (200 OK)**:

```json
{
  "job_id": "a1b2c3d4-e5f6-7890-1234-567890abcdef",
  "status": "completed",
  "created_at": "2026-02-28T12:00:00Z",
  "completed_at": "2026-02-28T12:01:30Z",
  "analysis_result": { ... }
}
```
