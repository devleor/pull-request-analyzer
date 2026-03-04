#!/bin/bash

# Comprehensive test of all three endpoints for MindsDB PR #9772
# This PR has 29 files changed with commit 5aa50591190a02384a3b2ba0320bec001ed273c1

echo "=================================================="
echo "    PULL REQUEST ANALYZER - ENDPOINTS TEST"
echo "=================================================="
echo ""
echo "Testing PR: MindsDB #9772"
echo "Commit: 5aa50591190a02384a3b2ba0320bec001ed273c1"
echo ""

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${YELLOW}ENDPOINT 1: GET /api/pull-requests/:owner/:repo/:number${NC}"
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""

# Fetch PR data
echo "Fetching PR data from GitHub..."
PR_DATA=$(curl -s -X GET "http://localhost:5000/api/pull-requests/mindsdb/mindsdb/9772")

if [ $? -eq 0 ] && [ -n "$PR_DATA" ]; then
    echo -e "${GREEN}✓ Successfully fetched PR data${NC}"
    echo ""
    echo "PR Details:"
    echo "├─ Number: $(echo "$PR_DATA" | jq -r '.number')"
    echo "├─ Title: $(echo "$PR_DATA" | jq -r '.title')"
    echo "├─ Author: $(echo "$PR_DATA" | jq -r '.author')"
    echo "├─ State: $(echo "$PR_DATA" | jq -r '.state')"
    echo "├─ Created: $(echo "$PR_DATA" | jq -r '.created_at')"
    echo "├─ Merged: $(echo "$PR_DATA" | jq -r '.merged_at')"
    echo "├─ Files Changed: $(echo "$PR_DATA" | jq -r '.files_changed // "N/A"')"
    echo "├─ Additions: $(echo "$PR_DATA" | jq -r '.additions')"
    echo "└─ Deletions: $(echo "$PR_DATA" | jq -r '.deletions')"

    # Show some files if available
    FILES_COUNT=$(echo "$PR_DATA" | jq '.files | length' 2>/dev/null)
    if [ "$FILES_COUNT" != "null" ] && [ "$FILES_COUNT" -gt 0 ]; then
        echo ""
        echo "Sample Files (first 5):"
        echo "$PR_DATA" | jq -r '.files[:5][] | "  • \(.filename)"' 2>/dev/null
    fi
else
    echo -e "✗ Failed to fetch PR data"
    exit 1
fi

echo ""
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${YELLOW}ENDPOINT 2: GET /api/pull-requests/:owner/:repo/:number/commits${NC}"
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""

# Fetch commits
echo "Fetching commits for PR #9772..."
COMMITS_DATA=$(curl -s -X GET "http://localhost:5000/api/pull-requests/mindsdb/mindsdb/9772/commits")

if [ $? -eq 0 ] && [ -n "$COMMITS_DATA" ]; then
    COMMIT_COUNT=$(echo "$COMMITS_DATA" | jq '. | length')
    echo -e "${GREEN}✓ Successfully fetched $COMMIT_COUNT commit(s)${NC}"
    echo ""
    echo "Commits:"

    # Display each commit with details
    echo "$COMMITS_DATA" | jq -r '.[] |
        "├─ [\(.sha[0:7])] \(.message | split("\n")[0])
│  Author: \(.author)
│  Date: \(.date)
│  Files: \(.files | length)"'

    # Show detailed info for the specific commit we're interested in
    TARGET_COMMIT=$(echo "$COMMITS_DATA" | jq -r '.[] | select(.sha | startswith("5aa5059"))')
    if [ -n "$TARGET_COMMIT" ]; then
        echo ""
        echo "Target Commit Details (5aa5059...):"
        echo "$TARGET_COMMIT" | jq -r '"  Files changed: \(.files | length)"'
        echo "  Sample files from this commit:"
        echo "$TARGET_COMMIT" | jq -r '.files[:3][] | "    • \(.filename) (+\(.additions)/-\(.deletions))"' 2>/dev/null
    fi
else
    echo -e "✗ Failed to fetch commits"
    exit 1
fi

echo ""
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${YELLOW}ENDPOINT 3: POST /api/analyze${NC}"
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""

# Clear any existing cache to ensure fresh analysis
echo "Clearing cache to ensure fresh analysis..."
docker exec pr-analyzer-redis redis-cli del "cache:analysis:mindsdb:mindsdb:9772" > /dev/null 2>&1

# Build the analysis request with both PR data and commits
echo "Building analysis request with PR data and commits..."
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
    state: .state,
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
echo "Sending analysis request..."
ANALYSIS_RESULT=$(curl -s -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d "$ANALYSIS_REQUEST")

if [ $? -eq 0 ] && [ -n "$ANALYSIS_RESULT" ]; then
    # Check if it's an error response
    if echo "$ANALYSIS_RESULT" | jq -e '.errors' > /dev/null 2>&1; then
        echo -e "✗ Analysis failed with validation errors:"
        echo "$ANALYSIS_RESULT" | jq '.'
        exit 1
    elif echo "$ANALYSIS_RESULT" | jq -e '.pr_number' > /dev/null 2>&1; then
        echo -e "${GREEN}✓ Analysis completed successfully${NC}"
        echo ""

        echo "Analysis Results:"
        echo "├─ PR Number: $(echo "$ANALYSIS_RESULT" | jq -r '.pr_number')"
        echo "├─ PR Title: $(echo "$ANALYSIS_RESULT" | jq -r '.pr_title')"
        echo "├─ Timestamp: $(echo "$ANALYSIS_RESULT" | jq -r '.analysis_timestamp')"
        echo "├─ Confidence Score: $(echo "$ANALYSIS_RESULT" | jq -r '.confidence_score')"
        echo "└─ Alignment: $(echo "$ANALYSIS_RESULT" | jq -r '.claimed_vs_actual.alignment_assessment')"

        echo ""
        echo "Executive Summary:"
        echo "$ANALYSIS_RESULT" | jq -r '.executive_summary[]? | "  • \(.)"'

        echo ""
        echo "Change Units:"
        CHANGE_UNITS=$(echo "$ANALYSIS_RESULT" | jq -r '.change_units[]?')
        if [ -n "$CHANGE_UNITS" ]; then
            echo "$ANALYSIS_RESULT" | jq -r '.change_units[]? |
                "  [\(.type)] \(.title)
    └─ Intent: \(.inferred_intent)
    └─ Confidence: \(.confidence_level)
    └─ Files: \(.affected_files | join(", "))"'
        else
            echo "  (No change units detected)"
        fi

        echo ""
        echo "Risks & Concerns:"
        echo "$ANALYSIS_RESULT" | jq -r '.risks_and_concerns[]? | "  • \(.)"'

        # Save full results
        echo "$ANALYSIS_RESULT" | jq '.' > full_analysis_result.json
        echo ""
        echo -e "${GREEN}Full analysis saved to: full_analysis_result.json${NC}"
    else
        echo -e "✗ Unexpected response format"
        echo "$ANALYSIS_RESULT" | jq '.'
        exit 1
    fi
else
    echo -e "✗ Analysis request failed"
    exit 1
fi

echo ""
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${GREEN}    ✓ ALL ENDPOINTS TESTED SUCCESSFULLY!${NC}"
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""

# Check if Langfuse traces are being sent
echo "Checking Langfuse integration..."
RECENT_LOGS=$(docker logs pr-analyzer-api 2>&1 | tail -20 | grep -c "OtlpTraceExporter.*200")
if [ "$RECENT_LOGS" -gt 0 ]; then
    echo -e "${GREEN}✓ OpenTelemetry traces successfully sent to Langfuse${NC}"
else
    echo -e "${YELLOW}⚠ No recent Langfuse trace exports detected${NC}"
fi

echo ""
echo "Test completed at: $(date)"