# Architecture - Pull Request Analyzer

## Overview

The Pull Request Analyzer uses a **unified service architecture** where both the API and background worker run in the same container instance, communicating through **MassTransit** and **RabbitMQ**.

```
┌─────────────────────────────────────────────────────────────┐
│                  Single Container Instance                  │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  ASP.NET Core Application                            │   │
│  │                                                       │   │
│  │  ┌────────────────────────────────────────────────┐  │   │
│  │  │  API Layer (HTTP Endpoints)                    │  │   │
│  │  │  - POST /api/v2/analyze-async                  │  │   │
│  │  │  - GET /api/v2/jobs/{jobId}                    │  │   │
│  │  │  - GET /api/v2/jobs                            │  │   │
│  │  └────────────────────────────────────────────────┘  │   │
│  │                      │                                 │   │
│  │                      │ (publishes command)             │   │
│  │                      ▼                                 │   │
│  │  ┌────────────────────────────────────────────────┐  │   │
│  │  │  MassTransit Bus                               │  │   │
│  │  │  (In-Process Message Broker)                   │  │   │
│  │  └────────────────────────────────────────────────┘  │   │
│  │                      │                                 │   │
│  │                      │ (routes to RabbitMQ)            │   │
│  │                      ▼                                 │   │
│  │  ┌────────────────────────────────────────────────┐  │   │
│  │  │  Background Worker Service                     │  │   │
│  │  │  (IHostedService)                              │  │   │
│  │  │                                                 │  │   │
│  │  │  ┌──────────────────────────────────────────┐  │  │   │
│  │  │  │  AnalyzePullRequestConsumer              │  │  │   │
│  │  │  │  - Consumes messages from RabbitMQ       │  │  │   │
│  │  │  │  - Processes PR analysis                 │  │  │   │
│  │  │  │  - Updates job status                    │  │  │   │
│  │  │  │  - Sends webhooks                        │  │  │   │
│  │  │  └──────────────────────────────────────────┘  │  │   │
│  │  └────────────────────────────────────────────────┘  │   │
│  │                                                       │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                              │
└─────────────────────────────────────────────────────────────┘
                              │
                              │ (AMQP Protocol)
                              ▼
                    ┌─────────────────────┐
                    │   RabbitMQ Broker   │
                    │   (External)        │
                    └─────────────────────┘
```

## Key Components

### 1. API Layer

**File**: `Controllers/AsyncAnalysisController.cs`

Handles HTTP requests from clients:

- **POST /api/v2/analyze-async**: Accepts PR data and webhook URL, creates a job, and publishes a command to the message queue
- **GET /api/v2/jobs/{jobId}**: Returns the current status and result of a job
- **GET /api/v2/jobs**: Lists all jobs

The API is **stateless** and doesn't process the analysis itself. It only:
1. Validates input
2. Creates a job record
3. Publishes a message to RabbitMQ
4. Returns immediately with a job ID

### 2. Message Bus (MassTransit + RabbitMQ)

**Configuration**: `Program.cs`

MassTransit is configured to use RabbitMQ as the transport:

```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AnalyzePullRequestConsumer>();
    
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host($"rabbitmq://{host}:{port}", h =>
        {
            h.Username(username);
            h.Password(password);
        });
        
        cfg.ConfigureEndpoints(context);
    });
});
```

**Benefits**:
- **Decoupling**: API and consumer are loosely coupled
- **Scalability**: Multiple consumers can process messages in parallel
- **Reliability**: Messages persist in RabbitMQ until processed
- **Monitoring**: RabbitMQ management UI shows queue depth and throughput

### 3. Background Worker Service

**File**: `Services/BackgroundWorkerService.cs`

An `IHostedService` that runs in the background:

```csharp
public class BackgroundWorkerService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Keeps the MassTransit consumer alive
        // Listens for messages from RabbitMQ
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
```

**Responsibilities**:
- Ensures the MassTransit bus stays active
- Logs worker status and lifecycle events
- Gracefully handles shutdown

### 4. Message Consumer

**File**: `Consumers/AnalyzePullRequestConsumer.cs`

Processes `AnalyzePullRequestCommand` messages:

```csharp
public class AnalyzePullRequestConsumer : IConsumer<AnalyzePullRequestCommand>
{
    public async Task Consume(ConsumeContext<AnalyzePullRequestCommand> context)
    {
        // 1. Update job status to "processing"
        // 2. Call LLMAnalysisService to analyze the PR
        // 3. Publish success/failure event
        // 4. Send webhook notification
        // 5. Update job status with results
    }
}
```

**Workflow**:
1. Receives a command from the queue
2. Updates job status to "processing"
3. Performs the LLM analysis (can take several seconds)
4. Publishes a completion event
5. Sends webhook notification if provided
6. Updates job status with results

### 5. Job Status Service

**File**: `Services/JobStatusService.cs`

Manages job lifecycle and persistence:

- **Create Job**: Creates a new job with "queued" status
- **Update Status**: Updates status as it progresses (queued → processing → completed/failed)
- **Get Status**: Retrieves current job state
- **Persistence**: Stores job data in files (can be replaced with database)

### 6. Webhook Service

**File**: `Services/WebhookService.cs`

Sends HTTP POST notifications to client-provided URLs when analysis completes:

```bash
POST https://client-webhook-url.com/pr-analysis

{
  "job_id": "...",
  "pr_number": 5678,
  "analysis_result": { ... },
  "completed_at": "2026-02-28T18:00:45Z"
}
```

## Message Flow

### 1. Client Submits Analysis Request

```
Client
  │
  ├─ POST /api/v2/analyze-async
  │  {
  │    "pull_request_data": { ... },
  │    "webhook_url": "https://..."
  │  }
  │
  └─> AsyncAnalysisController
      │
      ├─ Validate input
      ├─ Create job (status: "queued")
      ├─ Publish AnalyzePullRequestCommand
      │
      └─ Return 202 Accepted
         {
           "job_id": "550e8400-...",
           "status": "queued",
           "status_check_url": "/api/v2/jobs/550e8400-..."
         }
```

### 2. Background Worker Processes Message

```
RabbitMQ
  │
  ├─ AnalyzePullRequestCommand
  │
  └─> BackgroundWorkerService
      │
      └─> AnalyzePullRequestConsumer
          │
          ├─ Update job status to "processing"
          ├─ Call LLMAnalysisService
          │  │
          │  ├─ Fetch PR data
          │  ├─ Chunk diffs if needed
          │  ├─ Call OpenRouter LLM
          │  ├─ Parse response
          │  │
          │  └─ Return AnalysisResult
          │
          ├─ Publish PullRequestAnalyzedEvent
          ├─ Send webhook notification
          ├─ Update job status to "completed"
          │
          └─ Complete
```

### 3. Client Checks Job Status

```
Client
  │
  ├─ GET /api/v2/jobs/550e8400-...
  │
  └─> AsyncAnalysisController
      │
      ├─ Get job from JobStatusService
      │
      └─ Return 200 OK
         {
           "job_id": "550e8400-...",
           "status": "completed",
           "analysis_result": { ... }
         }
```

## Advantages of This Architecture

### 1. **Unified Deployment**
- Single container with both API and worker
- Simpler deployment and orchestration
- Shared resources (memory, CPU)

### 2. **Asynchronous Processing**
- API responds immediately (202 Accepted)
- Analysis happens in background without blocking
- Client can poll status or use webhooks

### 3. **Scalability**
- Multiple instances can run independently
- Each instance has its own worker
- RabbitMQ distributes messages across instances

### 4. **Resilience**
- If analysis fails, job is marked as failed
- Webhook notifications inform client of failures
- Job status is persisted for later retrieval

### 5. **Separation of Concerns**
- API handles HTTP concerns (routing, validation)
- Worker handles business logic (analysis, LLM calls)
- Clear message contracts between components

### 6. **Monitoring**
- RabbitMQ management UI shows queue depth
- Job status service tracks all jobs
- Logs show detailed processing steps

## Configuration

### Environment Variables

```bash
# RabbitMQ Configuration
RABBITMQ_HOST=localhost
RABBITMQ_PORT=5672
RABBITMQ_USERNAME=guest
RABBITMQ_PASSWORD=guest

# LLM Configuration
OPENROUTER_API_KEY=your_key_here

# GitHub Configuration
GITHUB_TOKEN=your_token_here
```

### appsettings.json

```json
{
  "RabbitMQ": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "guest",
    "Password": "guest"
  },
  "JobStatusDirectory": "./job_status"
}
```

## Running the Application

### Start Everything

```bash
make dev
```

This:
1. Starts RabbitMQ in Docker
2. Waits for RabbitMQ to be ready
3. Builds the project
4. Starts the API + background worker

### Check Status

```bash
# API health
curl http://localhost:5000/health

# RabbitMQ management
http://localhost:15672 (guest/guest)

# List all jobs
curl http://localhost:5000/api/v2/jobs
```

## Future Enhancements

### 1. **Separate Worker Service**
- Move consumer to a separate console application
- Deploy as a separate container
- Scale independently from API

### 2. **Database Persistence**
- Replace file-based job storage with database
- Enable complex queries on job history
- Support distributed deployments

### 3. **Advanced Messaging**
- Implement dead-letter queues for failed messages
- Add retry policies with exponential backoff
- Support message priorities

### 4. **Monitoring & Observability**
- Integrate with Prometheus for metrics
- Add distributed tracing with Jaeger
- Implement custom health checks

### 5. **Horizontal Scaling**
- Deploy multiple API + worker instances
- Use load balancer for API requests
- RabbitMQ handles message distribution

## Comparison: Unified vs. Separate Services

| Aspect | Unified | Separate |
|--------|---------|----------|
| **Deployment** | Single container | Multiple containers |
| **Complexity** | Simpler | More complex |
| **Resource Usage** | Shared | Isolated |
| **Scaling** | Scale both together | Scale independently |
| **Failure Isolation** | API failure affects worker | Isolated failures |
| **Development** | Easier to debug | Requires inter-service communication |

**Current Implementation**: Unified (simpler, good for MVP)

**Future**: Can evolve to separate services as needed
