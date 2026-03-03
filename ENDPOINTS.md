# API Endpoints Documentation

## Required Endpoints (from OBJECTIVE.MD)

All endpoints are implemented and working according to the requirements:

### 1. GET /api/pull-requests/:owner/:repo/:number
**Purpose**: Returns normalized PR JSON

**Example**:
```bash
curl http://localhost:5000/api/pull-requests/mindsdb/mindsdb/12248
```

**Response**: Complete PullRequestData JSON with:
- PR metadata (number, title, description, author, timestamps)
- List of commits
- Changed files with diffs
- All required fields per OBJECTIVE.MD

### 2. GET /api/pull-requests/:owner/:repo/:number/commits
**Purpose**: Returns commit list

**Example**:
```bash
curl http://localhost:5000/api/pull-requests/mindsdb/mindsdb/12248/commits
```

**Response**: Array of CommitData objects

### 3. POST /api/analyze
**Purpose**: Accepts PR JSON and returns structured analysis

**Example**:
```bash
# First get PR data
curl -s http://localhost:5000/api/pull-requests/mindsdb/mindsdb/12248 > pr.json

# Then analyze it
curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d @pr.json
```

**Response**: AnalysisResult JSON with:
- Executive summary (2-6 bullets)
- Structured list of change units with:
  - What changed
  - Where it changed
  - Why it likely changed (inferred intent)
  - Confidence level
- Risks or notable concerns
- Comparison between claimed vs actual changes

## Additional Endpoints (Bonus Features)

### Asynchronous Analysis
- `POST /api/v2/analyze-async` - Submit PR for background analysis
- `GET /api/v2/jobs/{jobId}` - Check job status
- `GET /api/v2/jobs` - List all jobs

### Health & Info
- `GET /health` - System health check
- `GET /info` - System information
- `GET /api/analyze/health` - Analysis service health

## Testing Script

Use the provided `test-api.sh` script to test all endpoints:

```bash
bash test-api.sh
```

This will test:
1. Health check
2. System info
3. GET pull request data
4. GET commits
5. POST /api/analyze (synchronous - REQUIRED)
6. POST /api/v2/analyze-async (asynchronous - bonus)

## Notes

- All required endpoints from OBJECTIVE.MD are implemented ✅
- The `/api` prefix is used consistently
- The endpoint names match exactly what was requested
- Additional features (async, caching, observability) are bonuses beyond requirements