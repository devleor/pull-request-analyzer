#!/bin/bash

echo "Testing fresh analysis for PR #9772..."

# Fetch PR with files
PR_DATA=$(curl -s "http://localhost:5000/api/pull-requests/mindsdb/mindsdb/9772")

# Show what files we have
echo "Files in PR:"
echo "$PR_DATA" | jq '.changed_files[] | {filename, additions, deletions}'

# Fetch commits
COMMITS_DATA=$(curl -s "http://localhost:5000/api/pull-requests/mindsdb/mindsdb/9772/commits")

# Clear cache
docker exec pr-analyzer-redis redis-cli del "cache:analysis:mindsdb:mindsdb:9772" > /dev/null 2>&1

# Build analysis request with CHANGED_FILES not FILES
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
    additions: .additions,
    deletions: .deletions,
    commits: $commits,
    changed_files: .changed_files,
    files: .changed_files
  }')
}
EOF
)

# Send analysis request
echo ""
echo "Sending analysis request..."
RESULT=$(curl -s -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d "$ANALYSIS_REQUEST")

# Check result
echo ""
echo "Analysis Result:"
echo "$RESULT" | jq '.'

# Save to file for inspection
echo "$RESULT" | jq '.' > fresh_analysis_9772.json

echo ""
echo "Full result saved to fresh_analysis_9772.json"