# **Mesmer** Take-Home Assignment - Founding Engineer

## **Overview**

Mesmer helps engineering leaders understand how work actually gets done by analyzing real engineering activity: commits, pull requests, reviews, tasks, and collaboration patterns.

In this take-home assignment, you will build a small, end-to-end prototype that:

- Ingests GitHub pull request + commit data
- Analyzes what was actually implemented (based on real diffs)
- Uses an LLM-based workflow
- Exposes the functionality via API endpoints

This is a practical engineering exercise. We care about clarity, reasoning, and implementation quality, not perfect accuracy.


## **Test Case Repository**

Use the **MindsDB** open source repository:

- Repository: `mindsdb/mindsdb`
- Your solution must support analyzing real pull requests and their commits.
- Example commit with significant changes (29 files):
    
    https://github.com/mindsdb/mindsdb/commit/5aa50591190a02384a3b2ba0320bec001ed273c1
    

You may demonstrate your solution on a small set of PRs, but the architecture should support analyzing arbitrary PRs from this repository.

---

## **What You Are Building**

You must build a system that:

1. Ingests pull request data (metadata + commits + file diffs)
2. Produces a structured analysis of what was actually implemented
3. Uses an LLM-based workflow as part of the analysis
4. Exposes the functionality via API endpoints

The goal is to understand real implementation work, not just summarize commit messages.

---

## **Requirements**

### **1) Pull Request JSON Input**

Your system must accept a normalized JSON input representing a pull request.

At minimum, it must include:

- PR metadata (number, title, description, author, timestamps)
- List of commits (sha, message, author, timestamp)
- Changed files (filename, status, additions, deletions)
- Patch/diff content (or retrievable source)

You must:

- Define your JSON schema
- Include at least one real PR JSON file generated from MindsDB
- Document how it was generated (GitHub API script or transformation script)

---

### **2) LLM-Based Analysis Workflow**

You must implement an LLM-powered workflow that analyzes:

- File paths
- Diff content
- Structural changes
- Behavior vs refactor changes
- Test coverage signals (if applicable)
- Other meaningful heuristics

The output must be structured and go beyond commit message summarization.

Your output should include:

- Executive summary (2–6 bullets)
- Structured list of “change units” / features / work items
    - What changed
    - Where it changed
    - Why it likely changed (inferred intent)
    - Confidence level or rationale
- Risks or notable concerns
- Comparison between:
    - What the PR/commit message claims
    - What the actual diffs indicate

Accuracy does not need to be perfect. Logical consistency and reasoning matter more.

---

### **3) API Endpoints**

Expose your system via a local API server.

Minimum required endpoints:

- GET /pull-requests/:owner/:repo/:number
    - Returns normalized PR JSON
- GET /pull-requests/:owner/:repo/:number/commits
    - Returns commit list
- POST /analyze
    - Accepts PR JSON
    - Returns structured analysis

It must run locally (e.g., localhost:8000).