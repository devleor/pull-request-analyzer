# Design Notes: Pull Request Analyzer

This document outlines the architectural decisions, LLM workflow structure, and tradeoffs made during the development of the Pull Request Analyzer prototype.

## 1. Architectural Decisions

### Technology Stack

-   **Backend**: **C# with ASP.NET Core 8** was chosen for its high performance, modern features, and robust ecosystem. It provides a strong foundation for building a scalable and maintainable API.
-   **GitHub API Client**: **`Octokit`** was selected as the official and most comprehensive library for interacting with the GitHub API in .NET. It simplifies data fetching and handles authentication and rate limiting gracefully.
-   **JSON Handling**: **`Newtonsoft.Json`** was used for its flexibility and powerful features in serializing and deserializing complex JSON structures, which is crucial for handling the varied data from the GitHub API and LLM responses.

### System Design

The architecture is designed around a **service-oriented approach**, separating concerns into distinct layers:

1.  **Controllers (`Controllers/`)**: Handle incoming HTTP requests, validate input, and orchestrate the workflow. They are kept lean and delegate business logic to services.
2.  **Services (`Services/`)**: Contain the core business logic.
    -   `GitHubIngestService`: Encapsulates all interactions with the GitHub API.
    -   `LLMAnalysisService`: Manages the entire LLM workflow, from prompt construction to response parsing.
    -   `DiffChunkingService`: Provides utilities for handling large diffs to avoid exceeding LLM token limits.
3.  **Models (`Models/`)**: Define the data structures used throughout the application, including the normalized `PullRequestData` and the structured `AnalysisResult`.

This separation makes the system easier to test, maintain, and extend.

### Caching Strategy

A simple **file-based caching** mechanism was implemented to store both the fetched PR data and the analysis results. This has several advantages for a local prototype:

-   **Reduces API Calls**: Avoids repeated requests to the GitHub and OpenRouter APIs, which saves time and respects rate limits.
-   **Improves Performance**: Subsequent requests for the same data are served instantly from the local file system.
-   **Cost-Effective**: Minimizes token usage on the LLM service.

For a production system, this would be replaced with a more robust caching solution like Redis.

## 2. LLM Workflow Structure

The LLM workflow is the core of the analysis and is structured as follows:

### a. Prompt Engineering

A detailed prompt is constructed to guide the LLM in its analysis. The prompt includes:

-   **Role-Playing**: The LLM is instructed to act as an "expert code reviewer."
-   **Contextual Information**: Key metadata from the pull request (title, description, commits) is provided to give the LLM a clear understanding of the PR's intent.
-   **Diff Snippets**: To ground the analysis in the actual code changes, snippets of the diffs are included. The diffs are truncated to keep the prompt within a reasonable size.
-   **Structured Output**: The LLM is explicitly instructed to provide its response in a specific JSON format, which is crucial for reliable parsing.

### b. Handling Large Diffs (Chunking)

Large pull requests with many files or large diffs can easily exceed the token limits of most LLMs. The `DiffChunkingService` addresses this with a multi-faceted strategy:

1.  **Token Estimation**: Before making an API call, the service estimates the total number of tokens required for the PR data.
2.  **File-Level Chunking**: If the total token count is too high, the list of changed files is split into smaller chunks that are processed sequentially. The results from each chunk would then be aggregated.
3.  **Patch Truncation**: For individual files with very large diffs, the patch content is truncated to a manageable size, ensuring that the most important parts (usually the beginning of the file) are included.
4.  **Summarization as a Fallback**: If a PR is exceptionally large, the service can create a summary of file changes (e.g., `[Diff truncated: +500/-200 changes]`) instead of including the full diffs.

### c. Response Parsing and Structuring

The response from the LLM is a JSON string. The `LLMAnalysisService` includes logic to:

-   **Extract JSON**: The service can extract the JSON content even if it's embedded within markdown code blocks (e.g., ` ```json ... ``` `).
-   **Deserialize to C# Objects**: The JSON is deserialized into the `AnalysisResult` C# class, providing a strongly-typed object to work with.
-   **Error Handling**: The parsing logic is wrapped in try-catch blocks to handle cases where the LLM returns a malformed or unexpected response.

## 3. Tradeoffs and Limitations

-   **Local vs. Production**: The current design is optimized for a local prototype. Features like authentication, robust error handling, and scalable infrastructure would need to be added for a production environment.
-   **Synchronous Analysis**: The `/analyze` endpoint is synchronous, meaning the client has to wait for the LLM analysis to complete. For a production system, this would be converted to an **asynchronous workflow** (e.g., using a message queue and webhooks) to provide a better user experience.
-   **LLM Accuracy**: The quality of the analysis is entirely dependent on the capabilities of the underlying LLM. The prompt is designed to be as clear as possible, but the LLM can still make mistakes or "hallucinate" details. The confidence score in the output is a nod to this uncertainty.
-   **Limited Context for Large PRs**: While chunking helps, it also means the LLM may not have the full context of all changes at once, which could affect the quality of the overall analysis for very large PRs.

## 4. Future Improvements

-   **Asynchronous Analysis**: Implement a job queue (e.g., RabbitMQ or Hangfire) to process analysis requests in the background and notify clients via webhooks.
-   **Advanced LLM Techniques**: Explore more advanced techniques like Retrieval-Augmented Generation (RAG) to provide the LLM with more context from the codebase, or use a multi-step agent-based approach for more detailed analysis.
-   **Database Integration**: Replace the file-based cache with a proper database (e.g., SQLite for local simplicity or PostgreSQL for production) to store and query PR data and analysis results more efficiently.
-   **Frontend UI**: Build a simple web interface (e.g., using React or Blazor) to visualize the analysis results and make the tool more interactive.
-   **Configuration for Prompts**: Move the LLM prompts to a separate configuration file to allow for easier iteration and A/B testing of different prompt strategies.
