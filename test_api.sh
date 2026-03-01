#!/bin/bash
set -e

API_BASE="http://localhost:5000/api"
OWNER="mindsdb"
REPO="mindsdb"
PR_NUMBER="5678"

echo "============================================"
echo "  Pull Request Analyzer — API Test Script"
echo "============================================"
echo ""

echo "[1] Health check"
curl -sf http://localhost:5000/health | jq .
echo ""

echo "[2] Upload PR data from example_pr_data.json"
curl -sf -X POST \
  -F "file=@example_pr_data.json" \
  "$API_BASE/pull-requests/$OWNER/$REPO/$PR_NUMBER/upload" | jq .
echo ""

echo "[3] GET /pull-requests/:owner/:repo/:number"
curl -sf "$API_BASE/pull-requests/$OWNER/$REPO/$PR_NUMBER" | jq '{number, title, author, changed_files_count}'
echo ""

echo "[4] GET /pull-requests/:owner/:repo/:number/commits"
curl -sf "$API_BASE/pull-requests/$OWNER/$REPO/$PR_NUMBER/commits" | jq 'length as $n | "Commits: \($n)"'
echo ""

echo "[5] POST /v2/analyze-async — submit analysis job"
JOB_RESPONSE=$(curl -sf -X POST \
  -H "Content-Type: application/json" \
  -d "{\"pull_request_data\": $(cat example_pr_data.json)}" \
  "$API_BASE/v2/analyze-async")
echo "$JOB_RESPONSE" | jq .
JOB_ID=$(echo "$JOB_RESPONSE" | jq -r '.job_id')
echo ""

echo "[6] GET /v2/jobs/:jobId — check job status"
echo "Waiting 3 seconds for analysis to process..."
sleep 3
curl -sf "$API_BASE/v2/jobs/$JOB_ID" | jq '{job_id, status, pr_number}'
echo ""

echo "[7] GET /v2/jobs — list all jobs"
curl -sf "$API_BASE/v2/jobs" | jq 'length as $n | "Total jobs: \($n)"'
echo ""

echo "============================================"
echo "  All tests completed!"
echo "============================================"
