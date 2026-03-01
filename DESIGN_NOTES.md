# Design Notes: Pull Request Analyzer

This document outlines the architectural decisions, LLM workflow structure, and tradeoffs made during the development of the Pull Request Analyzer prototype.

## 1. Architectural Decisions

### Technology Stack

-   **Backend**: **C# with ASP.NET Core 8** was chosen for its high performance, modern features, and robust ecosystem.
-   **GitHub API Client**: **`Octokit`** was selected as the official and most comprehensive library for interacting with the GitHub API in .NET.
-   **JSON Handling**: **`System.Text.Json`** is used for its high performance and native integration with ASP.NET Core.
-   **Cache, Queue, and Locking**: **Redis** is used for all three purposes, providing a unified and simple infrastructure:
    -   **Cache**: `StackExchange.Redis` for caching PR data, analysis results, and job status.
    -   **Queue**: **Redis Streams** for a reliable and persistent job queue.
    -   **Locking**: **`RedLock.net`** for distributed locking to prevent duplicate processing.

### System Design

The architecture is designed around a **service-oriented approach** with a **unified background worker**:

1.  **Controllers (`Controllers/`)**: Handle incoming HTTP requests, validate input, and publish jobs to the Redis queue.
2.  **Services (`Services/`)**: Contain the core business logic.
    -   `GitHubIngestService`: Encapsulates all interactions with the GitHub API.
    -   `LLMAnalysisService`: Manages the entire LLM workflow.
    -   `RedisBackgroundWorker`: An `IHostedService` that runs in the same process as the API, consuming jobs from the Redis Stream and orchestrating the analysis.
3.  **Models (`Models/`)**: Define the data structures, including `PullRequestData`, `AnalysisResult`, and the `Result<T>` pattern for error handling.

## 2. LLM Workflow Structure

(No changes to this section)

## 3. Tradeoffs and Limitations

-   **Local vs. Production**: The current design is optimized for a local prototype. For a production environment, you would run the API and worker in separate containers for independent scaling.
-   **LLM Accuracy**: The quality of the analysis is entirely dependent on the capabilities of the underlying LLM.
-   **Limited Context for Large PRs**: While chunking helps, it also means the LLM may not have the full context of all changes at once.

## 4. Future Improvements

-   **Separate Worker Service**: For production, move the `RedisBackgroundWorker` to a separate `Worker Service` project for independent scaling and deployment.
-   **Advanced LLM Techniques**: Explore Retrieval-Augmented Generation (RAG) or a multi-step agent-based approach for more detailed analysis.
-   **Frontend UI**: Build a simple web interface (e.g., using React or Blazor) to visualize the analysis results.
