# Design Notes

## Architecture Decisions

### 1. Technology Stack

**ASP.NET Core 8.0 + C#**
- **Why**: Enterprise-grade framework with excellent performance, strong typing, and robust ecosystem
- **Trade-off**: Larger container size vs interpreted languages
- **Alternative considered**: FastAPI (Python) - rejected due to better .NET integration with Semantic Kernel

**Microsoft Semantic Kernel**
- **Why**: Native .NET LLM orchestration framework with built-in prompt management
- **Trade-off**: Less mature than LangChain but better .NET integration
- **Alternative considered**: LangChain.NET - rejected due to limited features

**OpenRouter**
- **Why**: Multi-model gateway allowing easy model switching and cost optimization
- **Trade-off**: Additional latency vs direct API calls
- **Benefit**: Access to free models and unified API for multiple providers

### 2. LLM Workflow Structure

**Structured JSON Output**
- **Decision**: Force JSON response format with strict schema
- **Why**: Enables reliable parsing and validation
- **Trade-off**: Slightly constrains LLM creativity but ensures consistency

**Few-Shot Prompting**
- **Decision**: Include example in system prompt
- **Why**: Significantly improves output quality and format adherence
- **Trade-off**: Increases token usage (~500 extra tokens)

**Anti-Hallucination Validation**
- **Decision**: Validate all file references against actual PR files
- **Why**: Prevents LLM from inventing non-existent files
- **Implementation**: Post-processing validation layer

### 3. Large Diff Handling

**Truncation Strategy**
- **Decision**: Truncate file patches to 2000 characters each
- **Why**: Balances context vs token limits
- **Trade-off**: May miss some details in very large files
- **Future**: Implement semantic chunking based on AST

**File Prioritization**
- **Current**: Process files in order they appear
- **Future**: Prioritize by importance (tests, core logic, configs)

### 4. Caching Strategy

**Redis for Everything**
- **Decision**: Use Redis for cache, job queue, and distributed locks
- **Why**: Reduces infrastructure complexity
- **Trade-off**: Single point of failure
- **Mitigation**: Redis persistence enabled

**Cache Levels**
1. GitHub API responses: 1 hour TTL
2. Analysis results: 24 hour TTL
3. Prompt templates: No expiration for system prompts

### 5. Observability

**OpenTelemetry + Langfuse**
- **Decision**: Use OpenTelemetry as abstraction layer
- **Why**: Vendor-agnostic, can switch providers easily
- **Implementation**: Custom spans for LLM calls with input/output tracking

**Metrics Tracked**
- Token usage (input/output/total)
- Latency (end-to-end and LLM-specific)
- Cost estimation
- Confidence scores
- Validation failures

### 6. Error Handling

**Global Exception Middleware**
- **Decision**: Centralized error handling
- **Why**: Consistent error responses and logging
- **Features**: Correlation IDs, environment-aware responses

**Retry Strategy**
- GitHub API: 3 retries with exponential backoff
- LLM calls: No automatic retries (to control costs)
- Redis: Built-in retry in connection multiplexer

### 7. Production Features

**Rate Limiting**
- **Implementation**: Sliding window with Redis
- **Limits**: 10 requests/minute for /api/analyze
- **Why**: Prevent abuse and control costs

**Async Processing**
- **Decision**: Redis Streams for job queue
- **Why**: Simpler than RabbitMQ/Kafka for this scale
- **Pattern**: Request-Reply with webhook callbacks

### 8. Security Considerations

**No Secrets in Code**
- All sensitive data in environment variables
- .env file for local development
- Docker secrets for production

**Input Validation**
- JSON schema validation
- File path sanitization
- Webhook URL validation

## Trade-offs Made

### 1. Model Quality vs Cost
- **Decision**: Default to free model (liquid/lfm-2.5-1.2b-instruct:free)
- **Impact**: Lower quality but $0 cost
- **Mitigation**: Easy to switch models via environment variable

### 2. Synchronous vs Asynchronous
- **Decision**: Support both modes
- **Sync**: Better for small PRs, immediate feedback
- **Async**: Better for large PRs, webhook integration
- **Trade-off**: Added complexity for flexibility

### 3. Prompt Management
- **Decision**: Store prompts in Redis, not in code
- **Benefit**: Hot-reload without deployment
- **Trade-off**: Additional dependency
- **Mitigation**: Fallback to default prompts

## Known Limitations

### 1. Token Limits
- **Issue**: Large PRs may exceed context window
- **Current**: Truncation at 2000 chars per file
- **Impact**: May miss important changes in large files
- **Future**: Implement sliding window analysis

### 2. Language Support
- **Current**: Best with popular languages (Python, JS, Java)
- **Issue**: May struggle with domain-specific languages
- **Mitigation**: Could fine-tune or use specialized models

### 3. Binary Files
- **Current**: Skipped entirely
- **Impact**: Misses image/asset changes
- **Future**: Could describe file types and sizes

### 4. Merge Conflicts
- **Current**: Analyzes PR as-is
- **Issue**: Doesn't detect potential conflicts
- **Future**: Could check against target branch

## Future Improvements

### Near Term (1-2 weeks)
1. **Streaming Responses**: Use SSE for real-time analysis feedback
2. **Batch Processing**: Analyze multiple PRs in single request
3. **Diff Semantic Chunking**: Smart chunking based on code structure

### Medium Term (1-2 months)
1. **Multi-Model Voting**: Run multiple models and aggregate results
2. **Custom Prompts per Repo**: Repository-specific analysis rules
3. **Integration Tests**: Automated testing with real PR data
4. **Metrics Dashboard**: Grafana dashboard for system metrics

### Long Term (3-6 months)
1. **Fine-Tuning**: Custom model for PR analysis
2. **IDE Integration**: VS Code / IntelliJ plugins
3. **CI/CD Integration**: GitHub Actions, GitLab CI
4. **Historical Analysis**: Track PR quality over time
5. **Team Analytics**: Identify patterns across developers

## Performance Optimization

### Current Performance
- Small PR (<5 files): ~2 seconds
- Medium PR (5-20 files): ~5 seconds
- Large PR (>20 files): ~10 seconds

### Optimization Strategies
1. **Parallel Processing**: Analyze files in parallel
2. **Predictive Caching**: Pre-fetch likely PRs
3. **Edge Deployment**: Reduce latency to LLM providers
4. **Model Optimization**: Use smaller, faster models for simple PRs