#!/bin/bash

echo "Testing Pull Request Analysis with improved LLM workflow"
echo "========================================================="
echo ""

# First, fetch a real PR from MindsDB
echo "1. Fetching PR data from MindsDB..."
PR_DATA=$(curl -s http://localhost:5000/api/pull-requests/mindsdb/mindsdb/9876)

if [ -z "$PR_DATA" ]; then
    echo "Failed to fetch PR data. Is the API running?"
    exit 1
fi

echo "   ✓ PR data fetched successfully"
echo ""

# Save PR data to file
echo "$PR_DATA" > test_pr.json

# Trigger analysis (using /api/analyze with webhook for async)
echo "2. Triggering analysis..."
RESPONSE=$(curl -s -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d "{
    \"pull_request_data\": $PR_DATA,
    \"webhook_url\": \"http://localhost:5000/api/webhook-test\"
  }")

JOB_ID=$(echo "$RESPONSE" | jq -r '.job_id')

if [ -z "$JOB_ID" ] || [ "$JOB_ID" = "null" ]; then
    echo "Failed to get job ID. Response:"
    echo "$RESPONSE"
    exit 1
fi

echo "   ✓ Analysis job created: $JOB_ID"
echo ""

# Poll for job completion
echo "3. Waiting for analysis to complete..."
MAX_ATTEMPTS=30
ATTEMPT=0

while [ $ATTEMPT -lt $MAX_ATTEMPTS ]; do
    sleep 2
    STATUS_RESPONSE=$(curl -s http://localhost:5000/api/v2/jobs/$JOB_ID)
    STATUS=$(echo "$STATUS_RESPONSE" | jq -r '.status')

    echo -n "   Status: $STATUS"

    if [ "$STATUS" = "completed" ]; then
        echo " ✓"
        echo ""
        echo "4. Analysis completed successfully!"
        echo ""
        echo "=== ANALYSIS RESULT ==="
        echo "$STATUS_RESPONSE" | jq '.analysis_result'

        # Check for hallucination indicators
        echo ""
        echo "=== QUALITY CHECKS ==="

        # Check if files referenced exist in the PR
        REFERENCED_FILES=$(echo "$STATUS_RESPONSE" | jq -r '.analysis_result.change_units[].affected_files[]' | sort -u)
        ACTUAL_FILES=$(echo "$PR_DATA" | jq -r '.changed_files[].filename' | sort -u)

        echo "Files referenced in analysis:"
        echo "$REFERENCED_FILES"
        echo ""
        echo "Actual files in PR:"
        echo "$ACTUAL_FILES"
        echo ""

        # Check confidence levels
        CONFIDENCE_LEVELS=$(echo "$STATUS_RESPONSE" | jq -r '.analysis_result.change_units[].confidence_level')
        echo "Confidence levels used: $CONFIDENCE_LEVELS"

        # Check for discrepancies
        DISCREPANCIES=$(echo "$STATUS_RESPONSE" | jq '.analysis_result.claimed_vs_actual.discrepancies')
        echo "Discrepancies found: $DISCREPANCIES"

        exit 0
    elif [ "$STATUS" = "failed" ]; then
        echo " ✗"
        echo "Analysis failed!"
        echo "$STATUS_RESPONSE" | jq '.'
        exit 1
    else
        echo " (attempt $((ATTEMPT+1))/$MAX_ATTEMPTS)"
    fi

    ATTEMPT=$((ATTEMPT+1))
done

echo ""
echo "Analysis timed out after $MAX_ATTEMPTS attempts"
exit 1