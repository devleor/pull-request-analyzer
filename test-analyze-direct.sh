#!/bin/bash

echo "Testing Pull Request Analysis (Direct)"
echo "======================================"
echo ""

# Test with a simple Facebook React PR
echo "1. Fetching PR data..."
PR_DATA=$(curl -s http://localhost:5000/api/pull-requests/facebook/react/31926)

if [ -z "$PR_DATA" ]; then
    echo "Failed to fetch PR data. Is the API running?"
    exit 1
fi

echo "   ✓ PR data fetched"
echo ""

# Call /api/analyze directly (synchronous mode)
echo "2. Calling /api/analyze (synchronous mode)..."
RESPONSE=$(curl -s -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d "{
    \"pull_request_data\": $PR_DATA
  }")

# Check if we got an error
ERROR=$(echo "$RESPONSE" | jq -r '.error // empty')
if [ -n "$ERROR" ]; then
    echo "   ✗ Analysis failed: $ERROR"
    echo "$RESPONSE" | jq '.'
    exit 1
fi

# Check if we got analysis result
SUMMARY=$(echo "$RESPONSE" | jq -r '.executive_summary // empty')
if [ -n "$SUMMARY" ]; then
    echo "   ✓ Analysis completed successfully!"
    echo ""
    echo "=== ANALYSIS RESULT ==="
    echo "$RESPONSE" | jq '.'

    # Extract key metrics
    echo ""
    echo "=== KEY METRICS ==="
    echo "Confidence Score: $(echo "$RESPONSE" | jq -r '.confidence_score')"
    echo "Change Units: $(echo "$RESPONSE" | jq '.change_units | length')"
    echo "Alignment: $(echo "$RESPONSE" | jq -r '.claimed_vs_actual.alignment_assessment')"
else
    echo "   ✗ Unexpected response format"
    echo "$RESPONSE" | jq '.'
    exit 1
fi