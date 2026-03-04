#!/bin/bash

# Test script for MindsDB PR #9772 with actual GitHub data
# This PR has 29 files changed according to the commit

echo "======================================"
echo "Testing MindsDB PR #9772 Analysis"
echo "Commit: 5aa50591190a02384a3b2ba0320bec001ed273c1"
echo "======================================"
echo ""

# Step 1: Fetch PR data from GitHub
echo "Step 1: Fetching PR data from GitHub..."
echo "----------------------------------------"
PR_RESPONSE=$(curl -s -X GET "http://localhost:5000/api/pull-requests/mindsdb/mindsdb/9772")

if [ $? -eq 0 ]; then
    echo "✓ PR data fetched successfully"
    echo "$PR_RESPONSE" | jq '.title, .author, .files_changed'
else
    echo "✗ Failed to fetch PR data"
    exit 1
fi

echo ""

# Step 2: Fetch commits for the PR
echo "Step 2: Fetching commits for PR #9772..."
echo "-----------------------------------------"
COMMITS_RESPONSE=$(curl -s -X GET "http://localhost:5000/api/pull-requests/mindsdb/mindsdb/9772/commits")

if [ $? -eq 0 ]; then
    echo "✓ Commits fetched successfully"
    COMMIT_COUNT=$(echo "$COMMITS_RESPONSE" | jq '. | length')
    echo "Found $COMMIT_COUNT commit(s)"
    echo "$COMMITS_RESPONSE" | jq '.[0] | {sha: .sha, message: .message, files_changed: .files | length}'
else
    echo "✗ Failed to fetch commits"
    exit 1
fi

echo ""

# Step 3: Run the analysis using the fetched PR data
echo "Step 3: Running PR analysis..."
echo "-------------------------------"

# Extract PR data for analysis
PR_DATA=$(echo "$PR_RESPONSE" | jq '{
    number: .number,
    title: .title,
    owner: "mindsdb",
    repo: "mindsdb",
    author: .author,
    description: .description,
    created_at: .created_at,
    updated_at: .updated_at,
    merged_at: .merged_at,
    files_changed: .files_changed,
    additions: .additions,
    deletions: .deletions,
    commits: .commits,
    files: .files
}')

# Send to analysis endpoint
ANALYSIS_RESPONSE=$(curl -s -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d "{\"pull_request_data\": $PR_DATA}")

if [ $? -eq 0 ]; then
    echo "✓ Analysis completed successfully"
    echo ""
    echo "Analysis Results:"
    echo "-----------------"
    echo "$ANALYSIS_RESPONSE" | jq '.'
else
    echo "✗ Analysis failed"
    exit 1
fi

echo ""
echo "======================================"
echo "Test completed successfully!"
echo "======================================"