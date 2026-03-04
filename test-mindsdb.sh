#!/bin/bash

echo "========================================================="
echo "Testing Pull Request Analysis with MindsDB Repository"
echo "========================================================="
echo ""

# First, let's find a PR number that corresponds to the commit mentioned
# The commit 5aa50591190a02384a3b2ba0320bec001ed273c1 is likely part of a PR

echo "1. Fetching MindsDB PR #9876..."
PR_DATA=$(curl -s http://localhost:5000/api/pull-requests/mindsdb/mindsdb/9876)

if [ -z "$PR_DATA" ] || [[ "$PR_DATA" == *"error"* ]]; then
    echo "   PR #9876 not found, trying another PR..."
    # Try a different PR number
    PR_DATA=$(curl -s http://localhost:5000/api/pull-requests/mindsdb/mindsdb/9000)
fi

if [ -z "$PR_DATA" ] || [[ "$PR_DATA" == *"error"* ]]; then
    echo "   Failed to fetch PR. Let's list recent PRs..."
    # Try to get any recent PR
    echo "   Fetching recent closed PR..."
    PR_DATA=$(curl -s "https://api.github.com/repos/mindsdb/mindsdb/pulls?state=closed&per_page=1" | jq '.[0]')
    PR_NUMBER=$(echo "$PR_DATA" | jq -r '.number')

    if [ -n "$PR_NUMBER" ] && [ "$PR_NUMBER" != "null" ]; then
        echo "   Found PR #$PR_NUMBER, fetching full data..."
        PR_DATA=$(curl -s http://localhost:5000/api/pull-requests/mindsdb/mindsdb/$PR_NUMBER)
    fi
fi

if [ -z "$PR_DATA" ] || [[ "$PR_DATA" == *"error"* ]]; then
    echo "   ✗ Failed to fetch PR data"
    exit 1
fi

# Extract PR info
PR_NUMBER=$(echo "$PR_DATA" | jq -r '.number // 0')
PR_TITLE=$(echo "$PR_DATA" | jq -r '.title // "Unknown"')
FILES_COUNT=$(echo "$PR_DATA" | jq '.changed_files | length // 0')
COMMITS_COUNT=$(echo "$PR_DATA" | jq '.commits | length // 0')

echo "   ✓ PR #$PR_NUMBER fetched: \"$PR_TITLE\""
echo "     Files changed: $FILES_COUNT"
echo "     Commits: $COMMITS_COUNT"
echo ""

# Save PR data for inspection
echo "$PR_DATA" > mindsdb_pr_$PR_NUMBER.json
echo "   (PR data saved to mindsdb_pr_$PR_NUMBER.json)"
echo ""

# Call /api/analyze directly (synchronous mode)
echo "2. Analyzing PR with /api/analyze endpoint..."
echo "   Sending $(echo "$PR_DATA" | wc -c) bytes of PR data..."

START_TIME=$(date +%s)
RESPONSE=$(curl -s -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d "{
    \"pull_request_data\": $PR_DATA
  }")
END_TIME=$(date +%s)
DURATION=$((END_TIME - START_TIME))

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
    echo "   ✓ Analysis completed in ${DURATION}s"
    echo ""

    # Save analysis result
    echo "$RESPONSE" > mindsdb_analysis_$PR_NUMBER.json
    echo "   (Analysis saved to mindsdb_analysis_$PR_NUMBER.json)"
    echo ""

    echo "=== EXECUTIVE SUMMARY ==="
    echo "$RESPONSE" | jq -r '.executive_summary[]' | while read -r line; do
        echo "   • $line"
    done
    echo ""

    echo "=== KEY METRICS ==="
    echo "   Confidence Score: $(echo "$RESPONSE" | jq -r '.confidence_score')"
    echo "   Change Units: $(echo "$RESPONSE" | jq '.change_units | length')"
    echo "   Alignment: $(echo "$RESPONSE" | jq -r '.claimed_vs_actual.alignment_assessment')"
    echo ""

    echo "=== CHANGE UNITS ==="
    echo "$RESPONSE" | jq -r '.change_units[] | "   [\(.type)] \(.title)"'
    echo ""

    echo "=== RISKS & CONCERNS ==="
    echo "$RESPONSE" | jq -r '.risks_and_concerns[]' | while read -r line; do
        echo "   ⚠ $line"
    done
    echo ""

    # Check OpenTelemetry/Langfuse integration
    echo "3. Checking Langfuse integration..."
    LOGS=$(docker logs pr-analyzer-api 2>&1 | tail -20)
    if echo "$LOGS" | grep -q "200.*otel/v1/traces"; then
        echo "   ✓ Traces successfully sent to Langfuse"
        echo "     Check your Langfuse dashboard at: https://us.cloud.langfuse.com"
    else
        echo "   ⚠ Could not confirm Langfuse trace delivery"
    fi

else
    echo "   ✗ Unexpected response format"
    echo "$RESPONSE" | jq '.'
    exit 1
fi

echo ""
echo "========================================================="
echo "Analysis complete! Files saved:"
echo "  - mindsdb_pr_$PR_NUMBER.json (PR data)"
echo "  - mindsdb_analysis_$PR_NUMBER.json (Analysis result)"
echo "========================================================="