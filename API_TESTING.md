# API Testing Guide

## Table of Contents
- [Prerequisites](#prerequisites)
- [Quick Test](#quick-test)
- [Endpoint Testing](#endpoint-testing)
- [Example Requests](#example-requests)
- [Validation Testing](#validation-testing)
- [Performance Testing](#performance-testing)
- [Troubleshooting](#troubleshooting)

## Prerequisites

1. **Environment Variables** (.env file):
```bash
GITHUB_TOKEN=your_github_token
OPENROUTER_API_KEY=your_openrouter_key
OPENROUTER_MODEL=google/gemini-2.0-flash-exp:free
REDIS_URL=localhost:6379
```

2. **Start Services**:
```bash
make dev
# Or manually:
docker-compose up --build
```

3. **Verify Health**:
```bash
curl http://localhost:5000/health
```

## Quick Test

Use the provided test script for comprehensive testing:
```bash
bash test-api.sh
```

## Endpoint Testing

### 1. Health Check
```bash
# Basic health check
curl http://localhost:5000/health | jq '.'

# Expected response:
{
  "status": "healthy",
  "redis": {
    "status": "connected",
    "latency_ms": 0.23
  }
}
```

### 2. System Information
```bash
curl http://localhost:5000/info | jq '.'

# Expected response:
{
  "service": "Pull Request Analyzer",
  "version": "1.0.0",
  "environment": "Development",
  "redis_connected": true,
  "llm_provider": "OpenRouter",
  "llm_model": "google/gemini-2.0-flash-exp:free"
}
```

### 3. Fetch Pull Request Data
```bash
# Get PR data from GitHub
curl "http://localhost:5000/api/pull-requests/mindsdb/mindsdb/12248" | jq '.'

# Response structure:
{
  "pull_request_data": {
    "number": 12248,
    "title": "PR title",
    "description": "PR description",
    "created_at": "2024-12-20T10:30:00Z",
    "author": "username",
    "commits": [...],
    "changed_files": [
      {
        "filename": "path/to/file.py",
        "status": "modified",
        "additions": 10,
        "deletions": 5,
        "patch": "@@ diff content..."
      }
    ]
  }
}
```

### 4. Get PR Commits
```bash
curl "http://localhost:5000/api/pull-requests/mindsdb/mindsdb/12248/commits" | jq '.'

# Response:
[
  {
    "sha": "abc123...",
    "message": "feat: add new feature",
    "author": "username",
    "date": "2024-12-20T10:30:00Z"
  }
]
```

### 5. Synchronous Analysis
```bash
# Small PR (synchronous analysis)
curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d '{
    "pull_request_data": {
      "number": 12248,
      "title": "Fix null reference exception",
      "description": "Fixed a critical bug",
      "commits": [
        {
          "sha": "abc123",
          "message": "fix: handle null reference"
        }
      ],
      "changed_files": [
        {
          "filename": "src/UserService.cs",
          "status": "modified",
          "additions": 5,
          "deletions": 2,
          "patch": "@@ -45,8 +45,11 @@\n-if (user.Profile != null)\n+if (user?.Profile != null)\n{\n    return user.Profile;\n}\n+else\n+{\n+    return new Profile();\n+}"
        }
      ]
    }
  }' | jq '.'
```

### 6. Asynchronous Analysis with Webhook
```bash
# Large PR (async with webhook)
curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d '{
    "pull_request_data": {
      "number": 12249,
      "title": "Major refactoring",
      "description": "Refactored the entire module",
      "commits": [...],
      "changed_files": [...]
    },
    "webhook_url": "https://webhook.site/unique-url"
  }' | jq '.'

# Response:
{
  "job_id": "job_abc123",
  "status": "queued",
  "message": "Analysis job submitted. Results will be sent to webhook."
}
```

### 7. Submit Async Job (V2 API)
```bash
curl -X POST http://localhost:5000/api/v2/analyze-async \
  -H "Content-Type: application/json" \
  -d '{
    "pull_request_data": {...},
    "webhook_url": "https://webhook.site/unique-url"
  }' | jq '.'
```

### 8. Check Job Status
```bash
# Get specific job status
curl "http://localhost:5000/api/v2/jobs/job_abc123" | jq '.'

# Response:
{
  "job_id": "job_abc123",
  "status": "completed",
  "created_at": "2024-12-20T10:30:00Z",
  "completed_at": "2024-12-20T10:31:00Z",
  "pr_number": 12248,
  "result": {
    "pr_number": 12248,
    "pr_title": "Fix null reference exception",
    "executive_summary": [...],
    "change_units": [...],
    "claimed_vs_actual": {...}
  }
}
```

### 9. List All Jobs
```bash
curl "http://localhost:5000/api/v2/jobs" | jq '.'

# Response:
[
  {
    "job_id": "job_abc123",
    "status": "completed",
    "pr_number": 12248,
    "created_at": "2024-12-20T10:30:00Z"
  },
  {
    "job_id": "job_def456",
    "status": "processing",
    "pr_number": 12249,
    "created_at": "2024-12-20T10:35:00Z"
  }
]
```

## Example Requests

### Real GitHub PR Analysis
```bash
# Analyze a real MindsDB PR
PR_DATA=$(curl -s "http://localhost:5000/api/pull-requests/mindsdb/mindsdb/12248")

curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d "$PR_DATA" | jq '.'
```

### Testing Hallucination Detection
```bash
# Submit PR with intentionally mismatched claims
curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d '{
    "pull_request_data": {
      "number": 99999,
      "title": "Add authentication system",
      "description": "Implemented OAuth2 authentication",
      "commits": [
        {
          "sha": "test123",
          "message": "fix: typo in readme"
        }
      ],
      "changed_files": [
        {
          "filename": "README.md",
          "status": "modified",
          "additions": 1,
          "deletions": 1,
          "patch": "@@ -1,1 +1,1 @@\n-# Projct Name\n+# Project Name"
        }
      ]
    }
  }' | jq '.'
```

## Validation Testing

### Test File Validation
The system validates that all referenced files exist in the PR:
```bash
# This should trigger validation warnings if LLM references non-existent files
curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d '{
    "pull_request_data": {
      "number": 1,
      "title": "Test PR",
      "changed_files": [
        {
          "filename": "exists.js",
          "patch": "@@ -1 +1 @@\n-old\n+new"
        }
      ]
    }
  }' | jq '.validation_warnings'
```

### Test Evidence Grounding
The system requires evidence citations from actual diffs:
```bash
# Check that evidence field contains actual diff content
curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d @test-pr.json | jq '.change_units[].evidence'
```

### Test Confidence Levels
```bash
# Verify confidence levels are properly assigned
curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d @test-pr.json | jq '.change_units[] | {title: .title, confidence: .confidence_level, rationale: .rationale}'
```

## Performance Testing

### Cache Performance
```bash
# First request (cache miss)
time curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d @test-pr.json > /dev/null

# Second request (cache hit - should be < 100ms)
time curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d @test-pr.json > /dev/null
```

### Concurrent Requests
```bash
# Test concurrent analysis
for i in {1..5}; do
  curl -X POST http://localhost:5000/api/analyze \
    -H "Content-Type: application/json" \
    -d @test-pr-$i.json &
done
wait
```

### Large PR Testing
```bash
# Test with a large PR (>50 files)
curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d @large-pr.json \
  --max-time 120
```

## Troubleshooting

### Common Issues

1. **Connection Refused**
```bash
# Check if services are running
docker-compose ps

# Check logs
docker-compose logs api
docker-compose logs redis
```

2. **GitHub Rate Limiting**
```bash
# Check rate limit status
curl -H "Authorization: token $GITHUB_TOKEN" \
  https://api.github.com/rate_limit | jq '.'
```

3. **Redis Connection Issues**
```bash
# Test Redis connection
docker exec -it pull-request-analyzer-redis-1 redis-cli ping
# Should return: PONG
```

4. **LLM API Errors**
```bash
# Check OpenRouter API status
curl -H "Authorization: Bearer $OPENROUTER_API_KEY" \
  https://openrouter.ai/api/v1/models | jq '.'
```

### Debug Mode
```bash
# Enable detailed logging
export ASPNETCORE_ENVIRONMENT=Development
docker-compose up --build

# View detailed logs
docker-compose logs -f api | grep -E "ERROR|WARNING|DEBUG"
```

### Testing Webhooks
Use webhook.site for testing async callbacks:
1. Visit https://webhook.site
2. Copy your unique URL
3. Use it as webhook_url in async requests
4. Monitor incoming webhook calls in real-time

## Example Test Data Files

### test-pr.json
```json
{
  "pull_request_data": {
    "number": 12345,
    "title": "Fix user authentication bug",
    "description": "Fixed issue where users couldn't log in",
    "commits": [
      {
        "sha": "abc123",
        "message": "fix: resolve authentication issue"
      }
    ],
    "changed_files": [
      {
        "filename": "src/auth/login.js",
        "status": "modified",
        "additions": 15,
        "deletions": 5,
        "patch": "@@ -10,5 +10,15 @@\n-  if (password === user.password) {\n+  const hashedPassword = await bcrypt.hash(password, 10);\n+  if (await bcrypt.compare(password, user.hashedPassword)) {\n     return generateToken(user);\n+  } else {\n+    throw new Error('Invalid credentials');\n   }"
      }
    ]
  }
}
```

## Monitoring During Tests

### Watch Redis Activity
```bash
# Monitor Redis commands in real-time
docker exec -it pull-request-analyzer-redis-1 redis-cli monitor
```

### Watch Background Worker
```bash
# Monitor job processing
docker-compose logs -f api | grep "RedisBackgroundWorker"
```

### Check Cache Keys
```bash
# List all cache keys
docker exec -it pull-request-analyzer-redis-1 redis-cli keys '*'

# Get specific cache entry
docker exec -it pull-request-analyzer-redis-1 redis-cli get "cache:analysis:mindsdb:mindsdb:12248"
```

## Success Criteria

A successful test should demonstrate:

1. ✅ **Accurate Analysis**: Change units correctly identified
2. ✅ **Evidence Grounding**: All claims backed by diff quotes
3. ✅ **Confidence Levels**: Appropriate HIGH/MEDIUM/LOW assignments
4. ✅ **No Hallucinations**: No references to non-existent files
5. ✅ **Cache Working**: Second request returns instantly
6. ✅ **Async Processing**: Jobs complete and webhooks fire
7. ✅ **Alignment Assessment**: Correctly identifies claim vs actual discrepancies

## Load Testing

For production readiness testing:
```bash
# Install Apache Bench
apt-get install apache2-utils

# Test with 100 requests, 10 concurrent
ab -n 100 -c 10 -p test-pr.json -T application/json \
  http://localhost:5000/api/analyze
```

## Continuous Testing

Set up automated testing:
```bash
# Create test suite
cat > run-tests.sh << 'EOF'
#!/bin/bash
set -e

echo "Running API tests..."

# Health check
curl -f http://localhost:5000/health || exit 1

# Test each endpoint
./test-api.sh

echo "All tests passed!"
EOF

chmod +x run-tests.sh
./run-tests.sh
```