> [!WARNING]
> **Security Warning:** The user-provided GitHub token was exposed in the chat history. It is crucial to revoke this token immediately in your GitHub settings to prevent unauthorized access. A new token should be generated and used for any further interactions.

# Pull Request Analyzer

This project is a take-home assignment for a Founding Engineer role at Mesmer. It is an end-to-end prototype that ingests GitHub pull request data, analyzes the implementation using an LLM-based workflow, and exposes the functionality via a local API.

## Overview

The system is built with **C# and ASP.NET Core 8** and performs the following actions:

1.  **Ingests Pull Request Data**: Fetches PR metadata, commits, and file diffs from the GitHub API or accepts a normalized JSON file.
2.  **LLM-Based Analysis**: Uses a Large Language Model (via OpenRouter) to analyze the code changes, infer intent, and identify potential risks.
3.  **Exposes API Endpoints**: Provides a local API to access PR data and trigger the analysis.

---

## Features

- **Normalized PR Data**: Defines a clear JSON schema for pull requests.
- **GitHub API Integration**: Uses `Octokit` to fetch real data from GitHub.
- **LLM Workflow**: Implements a sophisticated analysis workflow with prompt engineering and response parsing.
- **Diff Chunking**: Handles large pull requests by chunking diffs to fit within LLM token limits.
- **Local API Server**: Exposes functionality through a clean and simple REST API.
- **Caching**: Caches PR data and analysis results locally to speed up subsequent requests.

---

## Project Structure

```
/PullRequestAnalyzer
├── Controllers/
│   ├── AnalysisController.cs       # API controller for analysis
│   └── PullRequestController.cs    # API controller for PR data
├── Models/
│   ├── AnalysisResult.cs         # C# class for the structured analysis output
│   └── PullRequestData.cs          # C# classes for the normalized PR JSON
├── Services/
│   ├── DiffChunkingService.cs      # Logic for handling large diffs
│   ├── GitHubIngestService.cs      # Service to fetch data from GitHub
│   └── LLMAnalysisService.cs         # Service for LLM-based analysis via OpenRouter
├── Properties/
│   └── launchSettings.json       # Launch settings for the API
├── wwwroot/                       # (Not used in this project)
├── appsettings.json               # Configuration for the application
├── example_pr_data.json           # An example of a normalized PR JSON file
├── GeneratePRData.cs              # A console script to generate PR data from GitHub
├── Program.cs                     # Main entry point for the API
├── PullRequestAnalyzer.csproj     # Project file with dependencies
├── README.md                      # This file
└── test_api.sh                    # A shell script to test the API endpoints
```

---

## Setup and Dependencies

### Prerequisites

- **.NET 8 SDK**: [Download & Install .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0)
- **cURL**: For running the test script.
- **jq**: For pretty-printing JSON in the terminal.

### Installation

1.  **Clone the repository**:

    ```bash
    git clone <repository_url>
    cd PullRequestAnalyzer
    ```

2.  **Restore dependencies**:

    The .NET dependencies will be restored automatically when you build or run the project. The main packages used are:
    - `Microsoft.AspNetCore.OpenApi`
    - `Octokit`
    - `Newtonsoft.Json`

---

## Environment Variables

To run the full functionality of the application, you need to configure the following environment variables:

-   **`GITHUB_TOKEN`**: A GitHub Personal Access Token with `repo` scope. This is required to fetch data from the GitHub API.
-   **`OPENROUTER_API_KEY`**: Your API key for OpenRouter.ai. This is required for the LLM analysis.

You can set them in your shell:

```bash
export GITHUB_TOKEN='your_github_token'
export OPENROUTER_API_KEY='your_openrouter_key'
```

Alternatively, you can add them to the `appsettings.json` file for local development (not recommended for production):

```json
{
  // ... other settings
  "GitHubToken": "your_github_token",
  "OpenRouterApiKey": "your_openrouter_key"
}
```

---

## How to Run

### 1. Run the API Server

Execute the following command from the root of the `PullRequestAnalyzer` directory:

```bash
dotnet run
```

The API will start on `http://localhost:5000` (or a similar port). You can access the Swagger UI for interactive API documentation at `http://localhost:5000/swagger`.

### 2. Generate PR JSON (Optional)

If you want to generate a new PR JSON file from a real GitHub pull request, you can use the `GeneratePRData` script. **This requires the `GITHUB_TOKEN` to be set.**

```bash
dotnet run --project . -- <github_token> <owner> <repo> <pr_number> <output_file.json>

# Example:
dotnet run --project . -- $GITHUB_TOKEN mindsdb mindsdb 8000 mindsdb_pr_8000.json
```

This will create a new JSON file with the data for the specified pull request.

### 3. Trigger the Analysis

Once the API is running, you can use the `test_api.sh` script to test the endpoints. This script uses the `example_pr_data.json` file included in the repository.

Make sure the script is executable:

```bash
chmod +x test_api.sh
```

Then run it:

```bash
./test_api.sh
```

The script will:
1.  Check the health and info endpoints.
2.  Upload the example PR data to the API's cache.
3.  Fetch the PR data and commits.
4.  Trigger the analysis endpoint.

---

## Example Request/Response

### POST /api/analyze

This endpoint accepts the normalized PR JSON and returns a structured analysis.

**Request Body** (`example_pr_data.json`):

```json
{
  "id": 1234567890,
  "number": 5678,
  "title": "Add support for distributed query optimization",
  // ... full PR data
}
```

**Success Response (200 OK)**:

```json
{
  "pr_number": 5678,
  "pr_title": "Add support for distributed query optimization",
  "analysis_timestamp": "2026-02-27T18:00:00Z",
  "executive_summary": [
    "Introduced a new QueryOptimizer class for cost-based query planning.",
    "Added a DistributedExecutor to handle query execution across multiple nodes.",
    "Integrated the new optimization and execution logic into the existing QueryPipeline.",
    "Added unit tests for the new QueryOptimizer."
  ],
  "change_units": [
    {
      "type": "feature",
      "title": "Query Optimizer Implementation",
      "description": "A new QueryOptimizer class was added to perform cost-based optimization of query plans.",
      "affected_files": ["mindsdb/query/optimizer/query_optimizer.py"],
      "inferred_intent": "To improve query performance by selecting more efficient execution plans.",
      "confidence_level": "high",
      "test_coverage_signal": "medium"
    }
    // ... other change units
  ],
  "risks_and_concerns": [
    "The distributed execution logic has limited error handling.",
    "Performance benchmarks are based on a synthetic workload and may not reflect real-world scenarios."
  ],
  "claimed_vs_actual": {
    "alignment_assessment": "aligned",
    "discrepancies": []
  }
}
```
