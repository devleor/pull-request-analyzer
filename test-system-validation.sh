#!/bin/bash

# System Validation Test Script
# Tests all critical features of the Pull Request Analyzer

set -e

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo "🔍 Pull Request Analyzer - System Validation Test"
echo "=================================================="

# Configuration
API_BASE="http://localhost:5000"
TIMEOUT=120

# Check if API is running
echo -e "\n${YELLOW}1. Checking API health...${NC}"
HEALTH_RESPONSE=$(curl -s -X GET "${API_BASE}/health" || echo "API_NOT_RUNNING")

if [[ "$HEALTH_RESPONSE" == "API_NOT_RUNNING" ]]; then
    echo -e "${RED}❌ API is not running. Please start the application first.${NC}"
    echo "Run: docker-compose up -d"
    exit 1
fi

echo "$HEALTH_RESPONSE" | jq '.'
echo -e "${GREEN}✅ API is healthy${NC}"

# Check Redis connection
echo -e "\n${YELLOW}2. Validating Redis connection...${NC}"
REDIS_STATUS=$(echo "$HEALTH_RESPONSE" | jq -r '.redis.status')
if [[ "$REDIS_STATUS" == "connected" ]]; then
    echo -e "${GREEN}✅ Redis is connected${NC}"
else
    echo -e "${RED}❌ Redis connection failed${NC}"
    exit 1
fi

# Check Langfuse configuration
echo -e "\n${YELLOW}3. Checking Langfuse observability...${NC}"
LANGFUSE_STATUS=$(echo "$HEALTH_RESPONSE" | jq -r '.langfuse.status')
echo "Langfuse status: $LANGFUSE_STATUS"

# Test rate limiting
echo -e "\n${YELLOW}4. Testing rate limiting...${NC}"
for i in {1..3}; do
    RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" -X GET "${API_BASE}/api/pull-requests/mindsdb/mindsdb/11944")
    if [[ "$RESPONSE" == "429" ]]; then
        echo -e "${GREEN}✅ Rate limiting is working (429 Too Many Requests)${NC}"
        break
    elif [[ "$RESPONSE" == "200" || "$RESPONSE" == "202" ]]; then
        echo "Request $i: Status $RESPONSE"
    fi
done

# Test PR analysis with real MindsDB PR
echo -e "\n${YELLOW}5. Testing PR analysis (MindsDB PR #11944)...${NC}"

ANALYSIS_REQUEST='{
  "pull_request_data": {
    "owner": "mindsdb",
    "repo": "mindsdb",
    "number": 11944
  }
}'

echo "Sending analysis request..."
START_TIME=$(date +%s)

ANALYSIS_RESPONSE=$(curl -s -X POST \
  "${API_BASE}/api/analyze" \
  -H "Content-Type: application/json" \
  -d "$ANALYSIS_REQUEST" \
  --max-time $TIMEOUT)

END_TIME=$(date +%s)
DURATION=$((END_TIME - START_TIME))

echo "Analysis completed in ${DURATION} seconds"

# Validate response structure
echo -e "\n${YELLOW}6. Validating analysis response...${NC}"

# Check if response is valid JSON
if ! echo "$ANALYSIS_RESPONSE" | jq empty 2>/dev/null; then
    echo -e "${RED}❌ Invalid JSON response${NC}"
    echo "$ANALYSIS_RESPONSE"
    exit 1
fi

# Extract and validate key fields
CHANGE_UNITS=$(echo "$ANALYSIS_RESPONSE" | jq '.change_units')
EXECUTIVE_SUMMARY=$(echo "$ANALYSIS_RESPONSE" | jq '.executive_summary')
CONFIDENCE_SCORE=$(echo "$ANALYSIS_RESPONSE" | jq '.confidence_score')
ALIGNMENT=$(echo "$ANALYSIS_RESPONSE" | jq -r '.claimed_vs_actual.alignment_assessment')

# Validate change_units is an array
if [[ $(echo "$CHANGE_UNITS" | jq 'type') != '"array"' ]]; then
    echo -e "${RED}❌ change_units is not an array${NC}"
    exit 1
fi
echo -e "${GREEN}✅ change_units is properly formatted as array${NC}"

# Validate affected_files contain real PR files
AFFECTED_FILES=$(echo "$CHANGE_UNITS" | jq -r '.[].affected_files[]' 2>/dev/null)
if [[ "$AFFECTED_FILES" == *"mindsdb"* ]]; then
    echo -e "${GREEN}✅ Real PR files detected in affected_files${NC}"
    echo "Sample files:"
    echo "$AFFECTED_FILES" | head -3
else
    echo -e "${RED}❌ No real PR files found in affected_files${NC}"
    echo "Found: $AFFECTED_FILES"
fi

# Check confidence score
if (( $(echo "$CONFIDENCE_SCORE > 0" | bc -l) )); then
    echo -e "${GREEN}✅ Confidence score: $CONFIDENCE_SCORE${NC}"
else
    echo -e "${YELLOW}⚠️  Low or missing confidence score${NC}"
fi

# Display summary
echo -e "\n${YELLOW}7. Analysis Summary:${NC}"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PR: MindsDB #11944 - PocketBase handler"
echo "Change Units: $(echo "$CHANGE_UNITS" | jq 'length')"
echo "Confidence: $CONFIDENCE_SCORE"
echo "Alignment: $ALIGNMENT"
echo "Executive Summary:"
echo "$EXECUTIVE_SUMMARY" | jq -r '.[]' | head -5

# Test async mode with webhook
echo -e "\n${YELLOW}8. Testing async mode with webhook...${NC}"

ASYNC_REQUEST='{
  "pull_request_data": {
    "owner": "mindsdb",
    "repo": "mindsdb",
    "number": 11944
  },
  "webhook_url": "https://webhook.site/test"
}'

ASYNC_RESPONSE=$(curl -s -X POST \
  "${API_BASE}/api/analyze" \
  -H "Content-Type: application/json" \
  -d "$ASYNC_REQUEST")

JOB_ID=$(echo "$ASYNC_RESPONSE" | jq -r '.job_id' 2>/dev/null)
STATUS=$(echo "$ASYNC_RESPONSE" | jq -r '.status' 2>/dev/null)

if [[ "$STATUS" == "queued" ]] && [[ -n "$JOB_ID" ]]; then
    echo -e "${GREEN}✅ Async mode working - Job ID: $JOB_ID${NC}"
else
    echo -e "${YELLOW}⚠️  Async mode response unexpected${NC}"
    echo "$ASYNC_RESPONSE" | jq '.'
fi

# Save analysis to file
echo -e "\n${YELLOW}9. Saving analysis results...${NC}"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
OUTPUT_FILE="pr_11944_validation_${TIMESTAMP}.json"
echo "$ANALYSIS_RESPONSE" | jq '.' > "$OUTPUT_FILE"
echo -e "${GREEN}✅ Analysis saved to: $OUTPUT_FILE${NC}"

# Final validation score
echo -e "\n${YELLOW}═══════════════════════════════════════${NC}"
echo -e "${GREEN}🎉 SYSTEM VALIDATION COMPLETE${NC}"
echo -e "${YELLOW}═══════════════════════════════════════${NC}"

TESTS_PASSED=0
TESTS_TOTAL=9

[[ "$REDIS_STATUS" == "connected" ]] && ((TESTS_PASSED++))
[[ $(echo "$CHANGE_UNITS" | jq 'type') == '"array"' ]] && ((TESTS_PASSED++))
[[ "$AFFECTED_FILES" == *"mindsdb"* ]] && ((TESTS_PASSED++))
[[ -n "$CONFIDENCE_SCORE" ]] && ((TESTS_PASSED++))
[[ -n "$ALIGNMENT" ]] && ((TESTS_PASSED++))
[[ -n "$EXECUTIVE_SUMMARY" ]] && ((TESTS_PASSED++))
[[ "$STATUS" == "queued" ]] && ((TESTS_PASSED++))
[[ -f "$OUTPUT_FILE" ]] && ((TESTS_PASSED++))
[[ "$HEALTH_RESPONSE" != "API_NOT_RUNNING" ]] && ((TESTS_PASSED++))

echo -e "Tests Passed: ${GREEN}$TESTS_PASSED/$TESTS_TOTAL${NC}"

if [[ $TESTS_PASSED -eq $TESTS_TOTAL ]]; then
    echo -e "${GREEN}✅ All systems operational!${NC}"
    exit 0
else
    echo -e "${YELLOW}⚠️  Some tests failed. Review the output above.${NC}"
    exit 1
fi