#!/bin/bash

# =============================================================================
# Test Script for MindsDB PR #11944
# GitHub URL: https://github.com/mindsdb/mindsdb/pull/11944
# =============================================================================

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m' # No Color

# Configuration
API_URL="http://localhost:5000"
OWNER="mindsdb"
REPO="mindsdb"
PR_NUMBER="11944"

echo ""
echo -e "${BOLD}${CYAN}╔══════════════════════════════════════════════════════════════════╗${NC}"
echo -e "${BOLD}${CYAN}║           PULL REQUEST ANALYZER - PR #11944 TEST                ║${NC}"
echo -e "${BOLD}${CYAN}╚══════════════════════════════════════════════════════════════════╝${NC}"
echo ""
echo -e "${YELLOW}Repository:${NC} $OWNER/$REPO"
echo -e "${YELLOW}PR Number:${NC}  #$PR_NUMBER"
echo -e "${YELLOW}GitHub URL:${NC} https://github.com/$OWNER/$REPO/pull/$PR_NUMBER"
echo -e "${YELLOW}Timestamp:${NC}  $(date '+%Y-%m-%d %H:%M:%S')"
echo ""

# ============================================================================
# STEP 1: Fetch PR Data from GitHub
# ============================================================================
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${BOLD}${YELLOW}[1/4] Fetching Pull Request Data${NC}"
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""

echo -e "Fetching PR #$PR_NUMBER from GitHub API..."
PR_DATA=$(curl -s -X GET "$API_URL/api/pull-requests/$OWNER/$REPO/$PR_NUMBER")

if [ $? -eq 0 ] && [ -n "$PR_DATA" ]; then
    # Check for error
    if echo "$PR_DATA" | jq -e '.error' > /dev/null 2>&1; then
        echo -e "${RED}✗ Error fetching PR:${NC}"
        echo "$PR_DATA" | jq -r '.error'
        exit 1
    fi

    echo -e "${GREEN}✓ Successfully fetched PR data${NC}"
    echo ""

    # Extract PR information
    PR_TITLE=$(echo "$PR_DATA" | jq -r '.title')
    PR_AUTHOR=$(echo "$PR_DATA" | jq -r '.author')
    PR_STATE=$(echo "$PR_DATA" | jq -r '.state')
    PR_CREATED=$(echo "$PR_DATA" | jq -r '.created_at')
    PR_UPDATED=$(echo "$PR_DATA" | jq -r '.updated_at')
    PR_MERGED=$(echo "$PR_DATA" | jq -r '.merged_at // "Not merged"')
    PR_ADDITIONS=$(echo "$PR_DATA" | jq -r '.additions')
    PR_DELETIONS=$(echo "$PR_DATA" | jq -r '.deletions')
    FILES_COUNT=$(echo "$PR_DATA" | jq '.changed_files | length' 2>/dev/null || echo "0")

    echo -e "${CYAN}Pull Request Information:${NC}"
    echo -e "┌─────────────────────────────────────────────────────────────────"
    echo -e "│ ${BOLD}Title:${NC}      $PR_TITLE"
    echo -e "│ ${BOLD}Author:${NC}     $PR_AUTHOR"
    echo -e "│ ${BOLD}State:${NC}      $PR_STATE"
    echo -e "│ ${BOLD}Created:${NC}    $PR_CREATED"
    echo -e "│ ${BOLD}Updated:${NC}    $PR_UPDATED"
    echo -e "│ ${BOLD}Merged:${NC}     $PR_MERGED"
    echo -e "│ ${BOLD}Statistics:${NC} +$PR_ADDITIONS additions, -$PR_DELETIONS deletions"
    echo -e "│ ${BOLD}Files:${NC}      $FILES_COUNT changed files"
    echo -e "└─────────────────────────────────────────────────────────────────"

    # Show description preview if available
    PR_DESC=$(echo "$PR_DATA" | jq -r '.description // "No description"' | head -3)
    if [ "$PR_DESC" != "No description" ] && [ -n "$PR_DESC" ]; then
        echo ""
        echo -e "${CYAN}Description Preview:${NC}"
        echo "$PR_DESC" | sed 's/^/  /'
        echo "  ..."
    fi

    # Show sample of changed files if available
    if [ "$FILES_COUNT" -gt 0 ]; then
        echo ""
        echo -e "${CYAN}Changed Files (first 5):${NC}"
        echo "$PR_DATA" | jq -r '.changed_files[:5][] | "  • \(.filename) (+\(.additions)/-\(.deletions))"' 2>/dev/null || echo "  (File details not available)"
        if [ "$FILES_COUNT" -gt 5 ]; then
            echo "  ... and $((FILES_COUNT - 5)) more files"
        fi
    fi
else
    echo -e "${RED}✗ Failed to fetch PR data${NC}"
    exit 1
fi

# ============================================================================
# STEP 2: Fetch Commits
# ============================================================================
echo ""
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${BOLD}${YELLOW}[2/4] Fetching Commits${NC}"
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""

echo -e "Fetching commits for PR #$PR_NUMBER..."
COMMITS_DATA=$(curl -s -X GET "$API_URL/api/pull-requests/$OWNER/$REPO/$PR_NUMBER/commits")

if [ $? -eq 0 ] && [ -n "$COMMITS_DATA" ]; then
    COMMIT_COUNT=$(echo "$COMMITS_DATA" | jq '. | length')
    echo -e "${GREEN}✓ Successfully fetched $COMMIT_COUNT commit(s)${NC}"
    echo ""

    if [ "$COMMIT_COUNT" -gt 0 ]; then
        echo -e "${CYAN}Commits:${NC}"
        # Show first 10 commits
        echo "$COMMITS_DATA" | jq -r '.[:10][] |
            "  [\(.sha[0:7])] \(.message | split("\n")[0] | .[0:60])\(if (. | length) > 60 then "..." else "" end)
    Author: \(.author // "unknown")
    Files: \(.files // [] | length)"' | while IFS= read -r line; do
            if [[ "$line" == "  ["* ]]; then
                echo -e "${BOLD}$line${NC}"
            else
                echo "$line"
            fi
        done

        if [ "$COMMIT_COUNT" -gt 10 ]; then
            echo ""
            echo "  ... and $((COMMIT_COUNT - 10)) more commits"
        fi
    fi
else
    echo -e "${RED}✗ Failed to fetch commits${NC}"
    exit 1
fi

# ============================================================================
# STEP 3: Analyze PR
# ============================================================================
echo ""
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${BOLD}${YELLOW}[3/4] Analyzing Pull Request${NC}"
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""

# Build analysis request
echo -e "Building analysis request..."
ANALYSIS_REQUEST=$(cat <<EOF
{
  "pull_request_data": $(echo "$PR_DATA" | jq --argjson commits "$COMMITS_DATA" '{
    number: .number,
    title: .title,
    owner: "'$OWNER'",
    repo: "'$REPO'",
    author: .author,
    description: .description,
    created_at: .created_at,
    updated_at: .updated_at,
    merged_at: .merged_at,
    state: .state,
    additions: .additions,
    deletions: .deletions,
    commits: $commits,
    changed_files: .changed_files
  }')
}
EOF
)

# Send analysis request
echo -e "Sending analysis request to LLM..."
START_TIME=$(date +%s)

ANALYSIS_RESULT=$(curl -s -X POST "$API_URL/api/analyze" \
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

        # ====================================================================
        # Display Analysis Results
        # ====================================================================
        echo -e "${CYAN}╔══════════════════════════════════════════════════════════════════╗${NC}"
        echo -e "${CYAN}║                      ANALYSIS RESULTS                           ║${NC}"
        echo -e "${CYAN}╚══════════════════════════════════════════════════════════════════╝${NC}"
        echo ""

        # Basic Info
        CONFIDENCE=$(echo "$ANALYSIS_RESULT" | jq -r '.confidence_score')
        ALIGNMENT=$(echo "$ANALYSIS_RESULT" | jq -r '.claimed_vs_actual.alignment_assessment')
        TIMESTAMP=$(echo "$ANALYSIS_RESULT" | jq -r '.analysis_timestamp')

        echo -e "${BOLD}Analysis Metadata:${NC}"
        echo -e "  • Confidence Score: ${YELLOW}$CONFIDENCE${NC}"
        echo -e "  • Alignment: ${GREEN}$ALIGNMENT${NC}"
        echo -e "  • Timestamp: $TIMESTAMP"

        # Executive Summary
        echo ""
        echo -e "${BOLD}${CYAN}Executive Summary:${NC}"
        echo "$ANALYSIS_RESULT" | jq -r '.executive_summary[]' | while IFS= read -r line; do
            echo -e "  ${GREEN}✓${NC} $line"
        done

        # Change Units
        echo ""
        echo -e "${BOLD}${CYAN}Change Units Detected:${NC}"
        CHANGE_UNITS=$(echo "$ANALYSIS_RESULT" | jq '.change_units | length')
        if [ "$CHANGE_UNITS" -gt 0 ]; then
            echo "$ANALYSIS_RESULT" | jq -r '.change_units[] | @json' | while IFS= read -r unit; do
                TYPE=$(echo "$unit" | jq -r '.type')
                TITLE=$(echo "$unit" | jq -r '.title')
                DESC=$(echo "$unit" | jq -r '.description')
                INTENT=$(echo "$unit" | jq -r '.inferred_intent')
                CONFIDENCE=$(echo "$unit" | jq -r '.confidence_level')
                FILES=$(echo "$unit" | jq -r '.affected_files | join(", ")')

                echo ""
                echo -e "  ${BOLD}[${TYPE^^}]${NC} $TITLE"
                echo -e "  ├─ ${YELLOW}Description:${NC} $DESC"
                echo -e "  ├─ ${YELLOW}Intent:${NC} $INTENT"
                echo -e "  ├─ ${YELLOW}Confidence:${NC} $CONFIDENCE"
                if [ "$FILES" != "" ] && [ "$FILES" != "null" ]; then
                    echo -e "  └─ ${YELLOW}Files:${NC} $FILES"
                else
                    echo -e "  └─ ${YELLOW}Files:${NC} (No specific files identified)"
                fi
            done
        else
            echo "  (No change units detected)"
        fi

        # Risks & Concerns
        echo ""
        echo -e "${BOLD}${CYAN}Risks & Concerns:${NC}"
        RISKS=$(echo "$ANALYSIS_RESULT" | jq '.risks_and_concerns | length')
        if [ "$RISKS" -gt 0 ]; then
            echo "$ANALYSIS_RESULT" | jq -r '.risks_and_concerns[]' | while IFS= read -r risk; do
                echo -e "  ${YELLOW}⚠${NC} $risk"
            done
        else
            echo -e "  ${GREEN}✓${NC} No significant risks identified"
        fi

        # Discrepancies
        echo ""
        echo -e "${BOLD}${CYAN}Claimed vs Actual Analysis:${NC}"
        DISCREPANCIES=$(echo "$ANALYSIS_RESULT" | jq '.claimed_vs_actual.discrepancies | length')
        if [ "$DISCREPANCIES" -gt 0 ]; then
            echo -e "  ${RED}⚠ Discrepancies found:${NC}"
            echo "$ANALYSIS_RESULT" | jq -r '.claimed_vs_actual.discrepancies[]' | while IFS= read -r disc; do
                echo "    • $disc"
            done
        else
            echo -e "  ${GREEN}✓${NC} No discrepancies - implementation matches description"
        fi

        # Save full results
        FILENAME="pr_${PR_NUMBER}_analysis_$(date +%Y%m%d_%H%M%S).json"
        echo "$ANALYSIS_RESULT" | jq '.' > "$FILENAME"
        echo ""
        echo -e "${GREEN}Full analysis saved to: $FILENAME${NC}"

        # Show complete JSON response
        echo ""
        echo -e "${CYAN}╔══════════════════════════════════════════════════════════════════╗${NC}"
        echo -e "${CYAN}║                    COMPLETE JSON RESPONSE                       ║${NC}"
        echo -e "${CYAN}╚══════════════════════════════════════════════════════════════════╝${NC}"
        echo ""
        echo "$ANALYSIS_RESULT" | jq '.'
    else
        echo -e "${RED}✗ Unexpected response format${NC}"
        echo "$ANALYSIS_RESULT" | jq '.'
        exit 1
    fi
else
    echo -e "${RED}✗ Analysis request failed${NC}"
    exit 1
fi

# ============================================================================
# STEP 4: Verify Langfuse Integration
# ============================================================================
echo ""
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${BOLD}${YELLOW}[4/4] Verifying Observability${NC}"
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""

echo "Checking OpenTelemetry/Langfuse integration..."
RECENT_TRACES=$(docker logs pr-analyzer-api 2>&1 | tail -50 | grep -c "OtlpTraceExporter.*200" || echo "0")
if [ "$RECENT_TRACES" -gt 0 ]; then
    echo -e "${GREEN}✓ Traces successfully sent to Langfuse ($RECENT_TRACES recent exports)${NC}"
else
    echo -e "${YELLOW}⚠ No recent Langfuse trace exports detected${NC}"
fi

# Check for any errors in the last few logs
ERROR_COUNT=$(docker logs pr-analyzer-api 2>&1 | tail -50 | grep -c "ERR" || echo "0")
if [ "$ERROR_COUNT" -gt 0 ]; then
    echo -e "${YELLOW}⚠ Found $ERROR_COUNT error(s) in recent logs${NC}"
fi

# ============================================================================
# Summary
# ============================================================================
echo ""
echo -e "${BOLD}${GREEN}════════════════════════════════════════════════════════════════════${NC}"
echo -e "${BOLD}${GREEN}                    ✓ TEST COMPLETED SUCCESSFULLY                  ${NC}"
echo -e "${BOLD}${GREEN}════════════════════════════════════════════════════════════════════${NC}"
echo ""
echo -e "${CYAN}Summary:${NC}"
echo -e "  • PR #$PR_NUMBER: $PR_TITLE"
echo -e "  • Analysis Time: ${ELAPSED}s"
echo -e "  • Change Units: $CHANGE_UNITS detected"
echo -e "  • Confidence: $CONFIDENCE"
echo -e "  • Alignment: $ALIGNMENT"
echo ""
echo -e "Test completed at: $(date '+%Y-%m-%d %H:%M:%S')"
echo ""