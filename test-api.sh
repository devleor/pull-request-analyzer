#!/bin/bash

# Test script for Pull Request Analyzer API
# Usage: bash test-api.sh

API_BASE="http://localhost:5000"

echo "========================================="
echo "Pull Request Analyzer API Tests"
echo "========================================="
echo ""

# 1. Health Check
echo "1. Testing Health Check..."
echo "   GET /health"
curl -s "$API_BASE/health" | jq '.'
echo ""
echo "-----------------------------------------"

# 2. Info Endpoint
echo "2. Testing Info Endpoint..."
echo "   GET /info"
curl -s "$API_BASE/info" | jq '.'
echo ""
echo "-----------------------------------------"

# 3. Get Pull Request Data
echo "3. Testing Get Pull Request..."
echo "   GET /api/pull-requests/mindsdb/mindsdb/12248"
curl -s "$API_BASE/api/pull-requests/mindsdb/mindsdb/12248" | jq '.'
echo ""
echo "-----------------------------------------"

# 4. Get Commits
echo "4. Testing Get Commits..."
echo "   GET /api/pull-requests/mindsdb/mindsdb/12248/commits"
curl -s "$API_BASE/api/pull-requests/mindsdb/mindsdb/12248/commits" | jq '.'
echo ""
echo "-----------------------------------------"

# 5. Test Synchronous Analysis (REQUIRED BY OBJECTIVE.MD)
echo "5. Testing Synchronous Analysis (POST /api/analyze)..."
echo "   POST /api/analyze"

# Save PR data to temp file if not already saved
if [ ! -f /tmp/pr_data.json ]; then
  curl -s "$API_BASE/api/pull-requests/mindsdb/mindsdb/12248" > /tmp/pr_data.json
fi

# Create request with just pull_request_data (synchronous mode)
jq -n --slurpfile pr /tmp/pr_data.json '{"pull_request_data": $pr[0]}' > /tmp/analyze_sync.json

# Submit synchronous analysis
echo "   Submitting PR for synchronous analysis (no webhook)..."
SYNC_RESPONSE=$(curl -s -X POST "$API_BASE/api/analyze" \
  -H "Content-Type: application/json" \
  -d @/tmp/analyze_sync.json)

echo "$SYNC_RESPONSE" | jq '.'
echo ""
echo "-----------------------------------------"

# 5b. Test Async via /api/analyze with webhook
echo "5b. Testing Async Mode via /api/analyze (with webhook_url)..."
echo "   POST /api/analyze with webhook_url"

# Create request with webhook_url (async mode)
jq -n --slurpfile pr /tmp/pr_data.json '{"pull_request_data": $pr[0], "webhook_url": "https://webhook.site/test"}' > /tmp/analyze_async.json

# Submit async analysis via /api/analyze
ASYNC_VIA_ANALYZE=$(curl -s -X POST "$API_BASE/api/analyze" \
  -H "Content-Type: application/json" \
  -d @/tmp/analyze_async.json)

echo "$ASYNC_VIA_ANALYZE" | jq '.'
echo ""
echo "-----------------------------------------"

# 6. Submit Async Analysis (Bonus feature)
echo "6. Testing Async Analysis (Bonus - POST /api/v2/analyze-async)..."
echo "   POST /api/v2/analyze-async"

# Save PR data to temp file
curl -s "$API_BASE/api/pull-requests/mindsdb/mindsdb/12248" > /tmp/pr_data.json

# Create request payload
jq -n --slurpfile pr /tmp/pr_data.json '{"pull_request_data": $pr[0], "webhook_url": null}' > /tmp/analyze_request.json

# Submit analysis
RESPONSE=$(curl -s -X POST "$API_BASE/api/v2/analyze-async" \
  -H "Content-Type: application/json" \
  -d @/tmp/analyze_request.json)

echo "$RESPONSE" | jq '.'

# Extract job ID
JOB_ID=$(echo "$RESPONSE" | jq -r '.job_id')
echo ""
echo "-----------------------------------------"

# 6. Check Job Status
if [ "$JOB_ID" != "null" ]; then
  echo "6. Testing Get Job Status..."
  echo "   GET /api/v2/jobs/$JOB_ID"

  # Wait a bit for processing
  sleep 2

  curl -s "$API_BASE/api/v2/jobs/$JOB_ID" | jq '.'
  echo ""
  echo "-----------------------------------------"
fi

# 7. List All Jobs
echo "7. Testing List All Jobs..."
echo "   GET /api/v2/jobs"
curl -s "$API_BASE/api/v2/jobs" | jq '.'
echo ""
echo "-----------------------------------------"

# 8. Test with different PR
echo "8. Testing with a different PR (smaller)..."
echo "   Fetching PR #12247"
curl -s "$API_BASE/api/pull-requests/mindsdb/mindsdb/12247" | jq '{number, title, additions, deletions}'
echo ""
echo "-----------------------------------------"

echo ""
echo "========================================="
echo "Tests completed!"
echo "========================================="

# Cleanup
rm -f /tmp/pr_data.json /tmp/analyze_request.json