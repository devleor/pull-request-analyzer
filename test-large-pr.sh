#!/bin/bash

echo "================================================"
echo "Testing Large PR Analysis (PR #9001)"
echo "================================================"
echo ""

# Fetch PR data
echo "1. Fetching MindsDB PR #9001 (19 files, 33 commits)..."
PR_DATA=$(curl -s http://localhost:5000/api/pull-requests/mindsdb/mindsdb/9001)

if [ -z "$PR_DATA" ] || [[ "$PR_DATA" == *"error"* ]]; then
    echo "Failed to fetch PR #9001"
    exit 1
fi

echo "$PR_DATA" > mindsdb_pr_9001.json
echo "   ✓ PR data saved to mindsdb_pr_9001.json"
echo ""

# Analyze
echo "2. Analyzing PR with /api/analyze endpoint..."
START_TIME=$(date +%s)
RESPONSE=$(curl -s -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d "{\"pull_request_data\": $PR_DATA}")
END_TIME=$(date +%s)
DURATION=$((END_TIME - START_TIME))

echo "$RESPONSE" > mindsdb_analysis_9001.json
echo "   ✓ Analysis completed in ${DURATION}s"
echo "   ✓ Analysis saved to mindsdb_analysis_9001.json"
echo ""

# Extract metrics
echo "=== ANALYSIS METRICS ==="
echo "$RESPONSE" | jq -r '
  "Change Units: \(.change_units | length)",
  "Confidence Score: \(.confidence_score)",
  "Files Analyzed: \(([.change_units[].affected_files[]] | unique | length))",
  "Change Types: \(([.change_units[].type] | unique | join(", ")))",
  "Risks Identified: \(.risks_and_concerns | length)"
'
echo ""

echo "=== EXECUTIVE SUMMARY ==="
echo "$RESPONSE" | jq -r '.executive_summary[]' | while read -r line; do
    echo "   • $line"
done
echo ""

echo "=== CHANGE UNITS BREAKDOWN ==="
echo "$RESPONSE" | jq -r '.change_units[] | "[\(.type)] \(.title) - \(.affected_files | length) file(s)"' | head -10
echo ""

# Check if analysis goes beyond commit messages
echo "=== DIFF ANALYSIS VALIDATION ==="
echo "Checking for evidence of actual diff analysis..."
EVIDENCE_COUNT=$(echo "$RESPONSE" | jq '[.change_units[].evidence // empty] | length')
RATIONALE_COUNT=$(echo "$RESPONSE" | jq '[.change_units[].rationale // empty] | length')
INTENT_COUNT=$(echo "$RESPONSE" | jq '[.change_units[].inferred_intent // empty] | length')

echo "   Evidence references: $EVIDENCE_COUNT"
echo "   Rationale provided: $RATIONALE_COUNT"
echo "   Intent inferred: $INTENT_COUNT"
echo ""

if [ "$EVIDENCE_COUNT" -gt 0 ]; then
    echo "   ✓ Analysis includes evidence from actual diffs"
else
    echo "   ⚠ No evidence from diffs found"
fi

echo ""
echo "================================================"
echo "Large PR analysis complete!"
echo "================================================"