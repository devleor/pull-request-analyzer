# Pull Request Analyzer - Async Architecture with MassTransit & RabbitMQ

This document describes the asynchronous, scalable architecture using **MassTransit** and **RabbitMQ** for processing pull request analyses.

## Architecture Overview

The system has been refactored to support **asynchronous processing** with the following components:

```
┌─────────────────────────────────────────────────────────────────┐
│                      API Server (ASP.NET Core)                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  AsyncAnalysisController                                │   │
│  │  - POST /api/v2/analyze-async  (submit job)             │   │
│  │  - GET  /api/v2/jobs/{jobId}   (check status)           │   │
│  │  - GET  /api/v2/jobs           (list all jobs)          │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ (publishes command)
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    RabbitMQ Message Broker                      │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Queue: AnalyzePullRequestCommand                        │   │
│  │  Exchange: PullRequestAnalyzer                           │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ (consumes command)
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│              Consumer Service (MassTransit)                     │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  AnalyzePullRequestConsumer                              │   │
│  │  - Processes analysis                                    │   │
│  │  - Updates job status                                    │   │
│  │  - Publishes completion events                           │   │
│  │  - Sends webhooks                                        │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ (publishes events)
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                  Event Subscribers                              │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  - JobStatusService (persists status)                    │   │
│  │  - WebhookService (notifies clients)                     │   │
│  │  - Other subscribers (logging, monitoring, etc.)         │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

## Key Components

### 1. Messages (`Messages/AnalyzePullRequestCommand.cs`)

Defines the command and events used in the message-driven architecture:

- **`AnalyzePullRequestCommand`**: Sent by the API to trigger analysis
- **`PullRequestAnalyzedEvent`**: Published when analysis completes successfully
- **`PullRequestAnalysisFailedEvent`**: Published when analysis fails

### 2. Consumer (`Consumers/AnalyzePullRequestConsumer.cs`)

The **`AnalyzePullRequestConsumer`** is a MassTransit consumer that:

1. Receives the `AnalyzePullRequestCommand` from the RabbitMQ queue
2. Updates the job status to "processing"
3. Calls the `LLMAnalysisService` to perform the analysis
4. Publishes a success or failure event
5. Sends a webhook notification if a URL was provided

### 3. Job Status Service (`Services/JobStatusService.cs`)

Manages the lifecycle of analysis jobs:

- **Create Job**: Creates a new job record with status "queued"
- **Update Status**: Updates job status and stores results
- **Get Status**: Retrieves the current status of a job
- **List Jobs**: Returns all jobs (for monitoring)

### 4. Webhook Service (`Services/WebhookService.cs`)

Sends HTTP POST notifications to client-provided webhook URLs when analysis completes.

### 5. Async Analysis Controller (`Controllers/AsyncAnalysisController.cs`)

Provides the API endpoints for asynchronous analysis:

- **`POST /api/v2/analyze-async`**: Submit a PR for analysis
- **`GET /api/v2/jobs/{jobId}`**: Check the status of a job
- **`GET /api/v2/jobs`**: List all jobs

## Setup and Running

### Prerequisites

- **Docker & Docker Compose**: To run RabbitMQ
- **.NET 8 SDK**: To run the application
- **Environment Variables**: `GITHUB_TOKEN`, `OPENROUTER_API_KEY`

### Step 1: Start RabbitMQ

```bash
docker-compose up -d
```

This starts RabbitMQ on `localhost:5672` and the management UI on `http://localhost:15672` (credentials: guest/guest).

### Step 2: Run the API

```bash
dotnet run
```

The API will connect to RabbitMQ and start listening for analysis commands.

### Step 3: Submit an Analysis Job

```bash
curl -X POST http://localhost:5000/api/v2/analyze-async \
  -H "Content-Type: application/json" \
  -d '{
    "pull_request_data": {
      "number": 5678,
      "title": "Add distributed query optimization",
      ...
    },
    "webhook_url": "https://your-webhook-endpoint.com/pr-analysis"
  }'
```

**Response** (202 Accepted):

```json
{
  "job_id": "550e8400-e29b-41d4-a716-446655440000",
  "status": "queued",
  "created_at": "2026-02-28T18:00:00Z",
  "status_check_url": "/api/v2/jobs/550e8400-e29b-41d4-a716-446655440000",
  "message": "Your analysis has been queued. Use the StatusCheckUrl to check the status."
}
```

### Step 4: Check Job Status

```bash
curl http://localhost:5000/api/v2/jobs/550e8400-e29b-41d4-a716-446655440000
```

**Response**:

```json
{
  "job_id": "550e8400-e29b-41d4-a716-446655440000",
  "status": "completed",
  "created_at": "2026-02-28T18:00:00Z",
  "started_at": "2026-02-28T18:00:05Z",
  "completed_at": "2026-02-28T18:00:45Z",
  "pr_number": 5678,
  "analysis_result": {
    "executive_summary": [...],
    "change_units": [...],
    ...
  }
}
```

## Webhook Notifications

When a job completes, the system sends a POST request to the provided webhook URL with the result:

```bash
POST https://your-webhook-endpoint.com/pr-analysis

{
  "job_id": "550e8400-e29b-41d4-a716-446655440000",
  "pr_number": 5678,
  "analysis_result": { ... },
  "completed_at": "2026-02-28T18:00:45Z"
}
```

## Scaling Considerations

The asynchronous architecture enables horizontal scaling:

1. **Multiple Consumers**: Run multiple instances of the consumer service to process jobs in parallel
2. **Load Balancing**: Use a load balancer to distribute API requests
3. **RabbitMQ Clustering**: Configure RabbitMQ in a cluster for high availability
4. **Database Persistence**: Replace file-based job status storage with a database (PostgreSQL, MongoDB, etc.)

Example scaling setup:

```
┌─────────────────────────────────────────────┐
│          Load Balancer                      │
└────────────┬────────────────────────────────┘
             │
    ┌────────┼────────┐
    ▼        ▼        ▼
┌────────┐ ┌────────┐ ┌────────┐
│ API #1 │ │ API #2 │ │ API #3 │  (stateless, can scale)
└────────┘ └────────┘ └────────┘
    │        │        │
    └────────┼────────┘
             ▼
    ┌─────────────────────┐
    │   RabbitMQ Cluster  │
    └─────────────────────┘
             │
    ┌────────┼────────┐
    ▼        ▼        ▼
┌────────┐ ┌────────┐ ┌────────┐
│Consumer│ │Consumer│ │Consumer│  (can scale independently)
│   #1   │ │   #2   │ │   #3   │
└────────┘ └────────┘ └────────┘
```

## Monitoring and Management

### RabbitMQ Management UI

Access the RabbitMQ management interface at `http://localhost:15672` (credentials: guest/guest).

You can:
- Monitor queue depths
- View message rates
- Inspect individual messages
- Manage connections and channels

### Job Status Monitoring

List all jobs to monitor the system:

```bash
curl http://localhost:5000/api/v2/jobs
```

## Future Enhancements

- **Persistent Database**: Store job history in a database for long-term tracking
- **Dead Letter Queue**: Handle failed messages with retry logic
- **Job Prioritization**: Support priority levels for urgent analyses
- **Batch Processing**: Process multiple PRs in a single batch
- **Metrics & Monitoring**: Integrate with Prometheus/Grafana for observability
- **Authentication**: Add API key or OAuth authentication
