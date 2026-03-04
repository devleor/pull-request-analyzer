#!/bin/bash

# Enhanced test script for PR #9772 with real GitHub data (29 files changed)

echo "=============================================="
echo "PR Analyzer Test - MindsDB PR #9772"
echo "Commit: 5aa50591190a02384a3b2ba0320bec001ed273c1"
echo "=============================================="
echo ""

# First, let's fetch the actual PR data from GitHub
echo "[1/3] Fetching PR #9772 data from GitHub..."
echo "---------------------------------------------"

PR_DATA=$(curl -s -X GET "http://localhost:5000/api/pull-requests/mindsdb/mindsdb/9772")

if [ $? -eq 0 ]; then
    echo "✓ PR data fetched successfully"

    # Display key PR information
    echo ""
    echo "PR Information:"
    echo "  Title: $(echo "$PR_DATA" | jq -r '.title')"
    echo "  Author: $(echo "$PR_DATA" | jq -r '.author')"
    echo "  Created: $(echo "$PR_DATA" | jq -r '.created_at')"
    echo "  Files Changed: $(echo "$PR_DATA" | jq -r '.files_changed')"
    echo "  Additions: $(echo "$PR_DATA" | jq -r '.additions')"
    echo "  Deletions: $(echo "$PR_DATA" | jq -r '.deletions')"

    # Show first few files
    echo ""
    echo "Sample of changed files:"
    echo "$PR_DATA" | jq -r '.files[:3][] | "  - \(.filename) (+\(.additions)/-\(.deletions))"' 2>/dev/null || echo "  (No file details available)"
else
    echo "✗ Failed to fetch PR data"
    exit 1
fi

echo ""
echo "[2/3] Fetching commits for PR #9772..."
echo "---------------------------------------"

COMMITS_DATA=$(curl -s -X GET "http://localhost:5000/api/pull-requests/mindsdb/mindsdb/9772/commits")

if [ $? -eq 0 ]; then
    COMMIT_COUNT=$(echo "$COMMITS_DATA" | jq '. | length')
    echo "✓ Found $COMMIT_COUNT commit(s)"

    echo ""
    echo "Commits:"
    echo "$COMMITS_DATA" | jq -r '.[] | "  [\(.sha[0:7])] \(.message | split("\n")[0])"'
else
    echo "✗ Failed to fetch commits"
    exit 1
fi

echo ""
echo "[3/3] Running analysis on PR..."
echo "--------------------------------"

# Build the analysis request with the actual PR data AND commits
ANALYSIS_REQUEST=$(cat <<EOF
{
  "pull_request_data": $(echo "$PR_DATA" | jq --argjson commits "$COMMITS_DATA" '{
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
    commits: $commits,
    files: .files
  }')
}
EOF
)

# Send the analysis request
ANALYSIS_RESULT=$(curl -s -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d "$ANALYSIS_REQUEST")

if [ $? -eq 0 ]; then
    echo "✓ Analysis completed"

    echo ""
    echo "=============================================="
    echo "Analysis Results:"
    echo "=============================================="

    # Display key results
    echo ""
    echo "Executive Summary:"
    echo "$ANALYSIS_RESULT" | jq -r '.executive_summary[] | "  • \(.)"'

    echo ""
    echo "Change Units Detected:"
    echo "$ANALYSIS_RESULT" | jq -r '.change_units[] | "  • [\(.type)] \(.title)"'
    echo "$ANALYSIS_RESULT" | jq -r '.change_units[] | "    Intent: \(.inferred_intent)"'
    echo "$ANALYSIS_RESULT" | jq -r '.change_units[] | "    Files: \(.affected_files | join(", "))"'

    echo ""
    echo "Confidence Score: $(echo "$ANALYSIS_RESULT" | jq -r '.confidence_score')"
    echo "Alignment: $(echo "$ANALYSIS_RESULT" | jq -r '.claimed_vs_actual.alignment_assessment')"

    # Save full results
    echo "$ANALYSIS_RESULT" | jq '.' > analysis_result.json
    echo ""
    echo "Full results saved to: analysis_result.json"
else
    echo "✗ Analysis failed"
    exit 1
fi

echo ""
echo "=============================================="
echo "Test completed successfully!"
echo "=============================================="