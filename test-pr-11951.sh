#!/bin/bash

# Test script for MindsDB PR #11951
# https://github.com/mindsdb/mindsdb/pull/11951

echo "=================================================="
echo "    TESTING MINDSDB PR #11951"
echo "=================================================="
echo ""

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${YELLOW}Step 1: Fetching PR #11951 from GitHub${NC}"
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""

# Fetch PR data
PR_DATA=$(curl -s -X GET "http://localhost:5000/api/pull-requests/mindsdb/mindsdb/11951")

if [ $? -eq 0 ] && [ -n "$PR_DATA" ]; then
    # Check for error
    if echo "$PR_DATA" | jq -e '.error' > /dev/null 2>&1; then
        echo -e "${RED}✗ Error fetching PR:${NC}"
        echo "$PR_DATA" | jq -r '.error'
        exit 1
    fi

    echo -e "${GREEN}✓ Successfully fetched PR data${NC}"
    echo ""
    echo "PR Information:"
    echo "├─ Number: $(echo "$PR_DATA" | jq -r '.number')"
    echo "├─ Title: $(echo "$PR_DATA" | jq -r '.title')"
    echo "├─ Author: $(echo "$PR_DATA" | jq -r '.author')"
    echo "├─ State: $(echo "$PR_DATA" | jq -r '.state')"
    echo "├─ Created: $(echo "$PR_DATA" | jq -r '.created_at')"
    echo "├─ Updated: $(echo "$PR_DATA" | jq -r '.updated_at')"
    echo "├─ Merged: $(echo "$PR_DATA" | jq -r '.merged_at // "Not merged"')"
    echo "├─ Files Changed: $(echo "$PR_DATA" | jq -r '.files_changed // "N/A"')"
    echo "├─ Additions: $(echo "$PR_DATA" | jq -r '.additions')"
    echo "├─ Deletions: $(echo "$PR_DATA" | jq -r '.deletions')"
    echo "└─ Description Preview: $(echo "$PR_DATA" | jq -r '.description // "No description" | .[0:100]')..."

    # Check if files are available
    FILES_COUNT=$(echo "$PR_DATA" | jq '.files | length' 2>/dev/null)
    if [ "$FILES_COUNT" != "null" ] && [ "$FILES_COUNT" -gt 0 ]; then
        echo ""
        echo "Changed Files (first 5):"
        echo "$PR_DATA" | jq -r '.files[:5][] | "  • \(.filename) (+\(.additions)/-\(.deletions))"' 2>/dev/null
    fi
else
    echo -e "${RED}✗ Failed to fetch PR data${NC}"
    exit 1
fi

echo ""
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${YELLOW}Step 2: Fetching commits for PR #11951${NC}"
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""

# Fetch commits
COMMITS_DATA=$(curl -s -X GET "http://localhost:5000/api/pull-requests/mindsdb/mindsdb/11951/commits")

if [ $? -eq 0 ] && [ -n "$COMMITS_DATA" ]; then
    COMMIT_COUNT=$(echo "$COMMITS_DATA" | jq '. | length')
    echo -e "${GREEN}✓ Successfully fetched $COMMIT_COUNT commit(s)${NC}"
    echo ""

    if [ "$COMMIT_COUNT" -gt 0 ]; then
        echo "Commits:"
        # Show first 5 commits
        echo "$COMMITS_DATA" | jq -r '.[:5][] |
            "├─ [\(.sha[0:7])] \(.message | split("\n")[0] | .[0:60])
│  Author: \(.author)
│  Files: \(.files | length)"'

        if [ "$COMMIT_COUNT" -gt 5 ]; then
            echo "└─ ... and $((COMMIT_COUNT - 5)) more commits"
        fi
    fi
else
    echo -e "${RED}✗ Failed to fetch commits${NC}"
    exit 1
fi

echo ""
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${YELLOW}Step 3: Analyzing PR #11951${NC}"
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""

# Clear cache for fresh analysis
echo "Clearing cache for fresh analysis..."
docker exec pr-analyzer-redis redis-cli del "cache:analysis:mindsdb:mindsdb:11951" > /dev/null 2>&1

# Build analysis request
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

# Send analysis request
echo "Sending analysis request..."
START_TIME=$(date +%s)
ANALYSIS_RESULT=$(curl -s -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d "$ANALYSIS_REQUEST")
END_TIME=$(date +%s)
ELAPSED=$((END_TIME - START_TIME))

if [ $? -eq 0 ] && [ -n "$ANALYSIS_RESULT" ]; then
    # Check for errors
    if echo "$ANALYSIS_RESULT" | jq -e '.error' > /dev/null 2>&1; then
        echo -e "${RED}✗ Analysis failed:${NC}"
        echo "$ANALYSIS_RESULT" | jq -r '.error'
        exit 1
    elif echo "$ANALYSIS_RESULT" | jq -e '.pr_number' > /dev/null 2>&1; then
        echo -e "${GREEN}✓ Analysis completed in ${ELAPSED} seconds${NC}"
        echo ""

        # Display results
        echo -e "${YELLOW}Analysis Results:${NC}"
        echo "┌─────────────────────────────────────────────────"
        echo "│ PR: #$(echo "$ANALYSIS_RESULT" | jq -r '.pr_number') - $(echo "$ANALYSIS_RESULT" | jq -r '.pr_title')"
        echo "│ Timestamp: $(echo "$ANALYSIS_RESULT" | jq -r '.analysis_timestamp')"
        echo "│ Confidence Score: $(echo "$ANALYSIS_RESULT" | jq -r '.confidence_score')"
        echo "│ Alignment: $(echo "$ANALYSIS_RESULT" | jq -r '.claimed_vs_actual.alignment_assessment')"
        echo "└─────────────────────────────────────────────────"

        echo ""
        echo -e "${YELLOW}Executive Summary:${NC}"
        echo "$ANALYSIS_RESULT" | jq -r '.executive_summary[]? | "  • \(.)"'

        echo ""
        echo -e "${YELLOW}Change Units Detected:${NC}"
        CHANGE_UNITS=$(echo "$ANALYSIS_RESULT" | jq '.change_units | length')
        if [ "$CHANGE_UNITS" -gt 0 ]; then
            echo "$ANALYSIS_RESULT" | jq -r '.change_units[] |
                "  ┌─ [\(.type | ascii_upcase)] \(.title)
  ├─ Description: \(.description)
  ├─ Intent: \(.inferred_intent)
  ├─ Confidence: \(.confidence_level)
  ├─ Rationale: \(.rationale)
  └─ Files: \(.affected_files | join(", "))
"'
        else
            echo "  (No change units detected)"
        fi

        echo -e "${YELLOW}Risks & Concerns:${NC}"
        RISKS=$(echo "$ANALYSIS_RESULT" | jq '.risks_and_concerns | length')
        if [ "$RISKS" -gt 0 ]; then
            echo "$ANALYSIS_RESULT" | jq -r '.risks_and_concerns[] | "  ⚠ \(.)"'
        else
            echo "  ✓ No significant risks identified"
        fi

        # Check for discrepancies
        echo ""
        echo -e "${YELLOW}Claimed vs Actual Analysis:${NC}"
        DISCREPANCIES=$(echo "$ANALYSIS_RESULT" | jq '.claimed_vs_actual.discrepancies | length')
        if [ "$DISCREPANCIES" -gt 0 ]; then
            echo "  ⚠ Discrepancies found:"
            echo "$ANALYSIS_RESULT" | jq -r '.claimed_vs_actual.discrepancies[] | "    • \(.)"'
        else
            echo "  ✓ No discrepancies - implementation matches description"
        fi

        # Save full results
        echo "$ANALYSIS_RESULT" | jq '.' > "pr_11951_analysis.json"
        echo ""
        echo -e "${GREEN}Full analysis saved to: pr_11951_analysis.json${NC}"
    else
        echo -e "${RED}✗ Unexpected response format${NC}"
        echo "$ANALYSIS_RESULT" | jq '.'
        exit 1
    fi
else
    echo -e "${RED}✗ Analysis request failed${NC}"
    exit 1
fi

echo ""
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"

# Check Langfuse integration
echo "Checking OpenTelemetry/Langfuse integration..."
RECENT_TRACES=$(docker logs pr-analyzer-api 2>&1 | tail -30 | grep -c "OtlpTraceExporter.*200")
if [ "$RECENT_TRACES" -gt 0 ]; then
    echo -e "${GREEN}✓ Traces successfully sent to Langfuse ($RECENT_TRACES recent exports)${NC}"
else
    echo -e "${YELLOW}⚠ No recent Langfuse trace exports detected${NC}"
fi

echo ""
echo -e "${GREEN}════════════════════════════════════════════════════${NC}"
echo -e "${GREEN}    ✓ TEST COMPLETED SUCCESSFULLY!${NC}"
echo -e "${GREEN}════════════════════════════════════════════════════${NC}"
echo ""
echo "Test completed at: $(date)"