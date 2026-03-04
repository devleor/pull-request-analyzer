#!/bin/bash

# Test script for MindsDB PR #12248

echo "Testing MindsDB PR #12248 analysis..."

# MindsDB PR #12248 - A real PR from MindsDB repository
curl -X POST http://localhost:5000/api/analyze \
  -H "Content-Type: application/json" \
  -d '{
    "pull_request_data": {
      "number": 12248,
      "title": "Improve VertexAI API implementation",
      "owner": "mindsdb",
      "repo": "mindsdb",
      "author": "mindsdb-contributor",
      "description": "This PR improves the VertexAI API implementation to handle chat completion responses more robustly.",
      "created_at": "2024-03-21T10:00:00Z",
      "updated_at": "2024-03-21T15:30:00Z",
      "merged_at": null
    }
  }' | jq .

echo "Check the analysis results above!"