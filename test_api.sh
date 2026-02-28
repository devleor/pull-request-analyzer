#!/bin/bash

# Test script for Pull Request Analyzer API
# This script demonstrates the API endpoints

API_BASE="http://localhost:5000/api"
OWNER="mindsdb"
REPO="mindsdb"
PR_NUMBER="5678"

echo "=========================================="
echo "Pull Request Analyzer - API Test Script"
echo "=========================================="
echo ""

# Test 1: Health check
echo "[1] Testing health endpoint..."
curl -s http://localhost:5000/health | jq .
echo ""

# Test 2: Info endpoint
echo "[2] Testing info endpoint..."
curl -s http://localhost:5000/info | jq .
echo ""

# Test 3: Upload PR data
echo "[3] Uploading PR data..."
curl -s -X POST \
  -F "file=@example_pr_data.json" \
  "$API_BASE/pull-requests/$OWNER/$REPO/$PR_NUMBER/upload" | jq .
echo ""

# Test 4: Get PR data
echo "[4] Getting PR data..."
curl -s "$API_BASE/pull-requests/$OWNER/$REPO/$PR_NUMBER" | jq .
echo ""

# Test 5: Get commits
echo "[5] Getting commits..."
curl -s "$API_BASE/pull-requests/$OWNER/$REPO/$PR_NUMBER/commits" | jq .
echo ""

# Test 6: Analyze PR (requires OPENROUTER_API_KEY)
echo "[6] Analyzing PR (requires OpenRouter API key)..."
curl -s -X POST \
  -H "Content-Type: application/json" \
  -d @example_pr_data.json \
  "$API_BASE/analyze" | jq .
echo ""

echo "=========================================="
echo "Tests completed!"
echo "=========================================="
