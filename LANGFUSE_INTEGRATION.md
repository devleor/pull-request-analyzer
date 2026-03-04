# Langfuse Integration - Production Ready

## ✅ What We've Implemented

### 1. **Complete Conversation Tracking in Langfuse**
Now Langfuse shows the FULL conversation:
- **System Prompt**: The instructions for the LLM (stored in Redis, never expires)
- **Few-Shot Example**: Example to guide response format (stored in Redis, never expires)
- **PR Data**: The actual commits and diffs being analyzed
- **AI Response**: The complete JSON analysis

### 2. **Redis-Based Prompt Management**
- System prompts are stored in Redis with **NO EXPIRATION**
- User prompts have 90-day TTL
- Version control for all prompts
- Easy updates without code changes

### 3. **Production-Ready Features**
- Rate limiting (10 requests/minute for /api/analyze)
- Global error handling with correlation IDs
- Structured logging with Serilog
- Health checks for Kubernetes
- Cost estimation per analysis

## 🚀 How to Use

### Step 1: Initialize Prompts in Redis
```bash
# Start the application
make dev

# Initialize the prompts (one-time setup)
curl -X POST http://localhost:5000/api/prompttemplate/initialize
```

This creates:
- `pr_analysis_system` - Main system prompt (never expires)
- `pr_analysis_fewshot` - Few-shot example (never expires)
- `pr_summary` - Summary generation (90 days TTL)
- `commit_analysis` - Commit analysis (90 days TTL)

### Step 2: View in Langfuse Dashboard

When you analyze a PR, go to your Langfuse dashboard and you'll see:

```json
{
  "input": [
    {
      "role": "system",
      "content": "You are an expert code reviewer..."
    },
    {
      "role": "user",
      "content": "Example PR: Fix authentication bug..."
    },
    {
      "role": "assistant",
      "content": "Understood. I will follow this format..."
    },
    {
      "role": "user",
      "content": "Analyze this pull request...\n[PR DATA WITH COMMITS AND DIFFS]"
    }
  ],
  "output": "{complete JSON response from AI}",
  "metadata": {
    "pr_owner": "mindsdb",
    "pr_repo": "mindsdb",
    "pr_number": 12345,
    "file_count": 10,
    "commit_count": 5,
    "cost_estimate": 0.0025
  },
  "usage": {
    "input": 2500,
    "output": 800,
    "total": 3300
  }
}
```

### Step 3: Update Prompts Without Code Changes

```bash
# Update the system prompt
curl -X POST http://localhost:5000/api/prompttemplate/pr_analysis_system \
  -H "Content-Type: application/json" \
  -d '{
    "template": "You are an expert code reviewer...[new prompt]",
    "version": "v1.1",
    "metadata": {
      "type": "system",
      "updated_by": "team",
      "change": "Added security focus"
    }
  }'
```

### Step 4: Monitor in Langfuse

You can now track:
- **Token Usage**: Exact tokens per PR analysis
- **Latency**: How long each analysis takes
- **Costs**: Estimated cost based on model
- **Success Rate**: Track failures and errors
- **Prompt Performance**: Compare different prompt versions

## 📊 What You See in Langfuse

### Trace View
```
pr_analysis_12345
├── Start: 2024-01-20 10:30:00
├── Duration: 3.2s
├── Model: claude-3.5-sonnet
├── Tokens: 3,300
├── Cost: $0.0025
└── Status: Success
```

### Generation Details
- **Input**: Full conversation with all messages
- **Output**: Complete AI response
- **Metadata**: PR details, file counts, correlation ID
- **Model Parameters**: temperature=0.2, max_tokens=4096

## 🎯 Key Benefits

### For Development
- **Debug Issues**: See exact prompts and responses
- **Optimize Prompts**: A/B test different versions
- **Track Performance**: Monitor latency and costs

### For Production
- **Cost Control**: Track token usage and costs
- **Quality Monitoring**: Ensure consistent analysis
- **Troubleshooting**: Correlation IDs link logs to traces

## 🔧 Configuration

### Environment Variables
```bash
# Already in your .env
LANGFUSE_PUBLIC_KEY=pk-lf-xxx
LANGFUSE_SECRET_KEY=sk-lf-xxx
LANGFUSE_BASE_URL=https://cloud.langfuse.com
```

### Prompt Storage
- System prompts: No expiration
- User prompts: 90-day TTL
- All prompts versioned in Redis

## 📝 Architecture

```
Request → API Controller
    ↓
SemanticKernelAnalysisServiceWithRedis
    ├── Load prompts from Redis
    ├── Build conversation structure
    ├── Track in Langfuse (full conversation)
    ├── Call LLM via Semantic Kernel
    ├── Log response to Langfuse
    └── Return analysis
```

## 🎉 Summary

Your PR Analyzer now has:
1. **Full conversation tracking** in Langfuse (system, few-shot, data, response)
2. **Redis-based prompts** that never expire for system prompts
3. **Production-ready observability** with detailed metrics
4. **No hardcoded prompts** - everything configurable via Redis

The system properly tracks:
- What instructions the LLM received (system prompt)
- What example it was shown (few-shot)
- What data it analyzed (PR commits/diffs)
- What it responded (complete analysis)

This gives you complete visibility into your LLM operations! 🚀