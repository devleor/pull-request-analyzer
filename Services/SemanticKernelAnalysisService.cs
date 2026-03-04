#pragma warning disable SKEXP0010

using System.Diagnostics;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using PullRequestAnalyzer.Configuration;
using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services;

/// <summary>
/// Analysis service using Microsoft Semantic Kernel for LLM orchestration
/// </summary>
public sealed class SemanticKernelAnalysisService : IAnalysisService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly IPromptService _promptService;
    private readonly IJsonParsingService _jsonParser;
    private readonly IValidationService _validationService;
    private readonly AnalysisConfiguration _config;
    private readonly ILogger<SemanticKernelAnalysisService> _logger;
    private readonly string _modelName;

    private static readonly ActivitySource ActivitySource = new("PullRequestAnalyzer", "1.0.0");

    public SemanticKernelAnalysisService(
        IPromptService promptService,
        IJsonParsingService jsonParser,
        IValidationService validationService,
        IConfiguration configuration,
        ILogger<SemanticKernelAnalysisService> logger)
    {
        _promptService = promptService;
        _jsonParser = jsonParser;
        _validationService = validationService;
        _logger = logger;

        _config = configuration.GetSection("Analysis").Get<AnalysisConfiguration>()
                  ?? new AnalysisConfiguration();

        _modelName = Environment.GetEnvironmentVariable("OPENROUTER_MODEL")
                    ?? configuration["OpenRouter:Model"]
                    ?? "liquid/lfm-2.5-1.2b-instruct:free";

        _kernel = BuildKernel(configuration);
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();

        _logger.LogInformation("Analysis service initialized with model: {Model}", _modelName);
    }

    public async Task<AnalysisResult> AnalyzeAsync(PullRequestData pr)
    {
        using var activity = StartActivity(pr);
        var correlationId = new TraceId();

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId.Value,
            ["PR"] = new PullRequestId(pr.Owner, pr.Repo, pr.Number).ToString(),
            ["Operation"] = "PRAnalysis"
        });

        try
        {
            _logger.LogInformation("Starting PR analysis - Files: {FileCount}, Commits: {CommitCount}",
                pr.ChangedFiles.Count, pr.Commits.Count);

            var chatHistory = await BuildChatHistoryAsync(pr);
            var response = await ExecuteLlmCallAsync(chatHistory, activity);
            var result = ProcessResponse(response, pr);

            LogAnalysisCompletion(result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private Kernel BuildKernel(IConfiguration configuration)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
                     ?? configuration["OpenRouter:ApiKey"]
                     ?? throw new InvalidOperationException("OPENROUTER_API_KEY is not configured");

        var builder = Kernel.CreateBuilder();

        builder.AddOpenAIChatCompletion(
            modelId: _modelName,
            apiKey: apiKey,
            endpoint: new Uri("https://openrouter.ai/api/v1"),
            httpClient: new HttpClient
            {
                Timeout = _config.Llm.Timeout,
                DefaultRequestHeaders =
                {
                    { "HTTP-Referer", "https://github.com/devleor/pull-request-analyzer" },
                    { "X-Title", "PullRequestAnalyzer" }
                }
            });

        return builder.Build();
    }

    private Activity? StartActivity(PullRequestData pr)
    {
        var activity = ActivitySource.StartActivity(
            $"pr_analysis.{pr.Number}",
            ActivityKind.Internal);

        activity?.SetTag("pr.owner", pr.Owner);
        activity?.SetTag("pr.repo", pr.Repo);
        activity?.SetTag("pr.number", pr.Number);
        activity?.SetTag("pr.title", pr.Title);
        activity?.SetTag("pr.author", pr.Author);
        activity?.SetTag("pr.file_count", pr.ChangedFiles.Count);
        activity?.SetTag("pr.commit_count", pr.Commits.Count);
        activity?.SetTag("llm.model", _modelName);

        return activity;
    }

    private async Task<ChatHistory> BuildChatHistoryAsync(PullRequestData pr)
    {
        var systemPrompt = await _promptService.GetSystemPromptAsync();
        var prContent = _promptService.BuildPullRequestContent(pr);

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage(prContent);

        LogPromptInfo(pr.ChangedFiles);

        return chatHistory;
    }

    private async Task<string> ExecuteLlmCallAsync(ChatHistory chatHistory, Activity? activity)
    {
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            Temperature = _config.Llm.Temperature,
            MaxTokens = _config.Llm.MaxTokens
        };

        using var llmSpan = StartLlmSpan();

        // Record the prompt in the span for Langfuse
        var messages = chatHistory.Select(m => new
        {
            role = m.Role.ToString().ToLower(),
            content = m.Content
        }).ToArray();

        var promptJson = System.Text.Json.JsonSerializer.Serialize(messages);
        llmSpan?.SetTag("gen_ai.prompt", promptJson);

        // Estimate prompt tokens
        var totalPromptLength = messages.Sum(m => m.content?.Length ?? 0);
        llmSpan?.SetTag("gen_ai.usage.prompt_tokens", totalPromptLength / _config.Llm.PromptTokensEstimate);

        var stopwatch = Stopwatch.StartNew();

        var response = await _chatService.GetChatMessageContentAsync(
            chatHistory,
            executionSettings,
            _kernel);

        stopwatch.Stop();

        // Record the completion in the span for Langfuse
        llmSpan?.SetTag("gen_ai.completion", response.Content);

        RecordLlmMetrics(llmSpan, activity, response.Content, stopwatch.ElapsedMilliseconds);

        return response.Content ?? throw new InvalidOperationException("Empty response from LLM");
    }

    private AnalysisResult ProcessResponse(string rawResponse, PullRequestData pr)
    {
        var result = _jsonParser.ParseLlmResponse(rawResponse, pr);
        var validation = _validationService.ValidateAnalysis(result, pr);

        if (!validation.IsValid)
        {
            _logger.LogWarning("Analysis contains unverified content - Issues: {Issues}",
                string.Join(", ", validation.Issues));
        }

        return result;
    }

    private Activity? StartLlmSpan()
    {
        var span = ActivitySource.StartActivity("generation", ActivityKind.Client);
        span?.SetTag("gen_ai.system", "openrouter");
        span?.SetTag("gen_ai.request.model", _modelName);
        span?.SetTag("gen_ai.request.temperature", _config.Llm.Temperature);
        span?.SetTag("gen_ai.request.max_tokens", _config.Llm.MaxTokens);
        return span;
    }

    private void RecordLlmMetrics(Activity? span, Activity? parentActivity, string? response, long latencyMs)
    {
        if (response == null) return;

        var outputTokens = response.Length / _config.Llm.PromptTokensEstimate;

        // Get prompt tokens from span if available
        var promptTokens = 0;
        if (span?.GetTagItem("gen_ai.usage.prompt_tokens") is int pt)
        {
            promptTokens = pt;
        }

        var totalTokens = promptTokens + outputTokens;
        var cost = EstimateCost(totalTokens);

        span?.SetTag("gen_ai.usage.completion_tokens", outputTokens);
        span?.SetTag("gen_ai.usage.total_tokens", totalTokens);
        span?.SetTag("gen_ai.response.finish_reasons", new[] { "stop" });
        span?.SetStatus(ActivityStatusCode.Ok);

        parentActivity?.SetTag("llm.prompt_tokens_estimate", promptTokens);
        parentActivity?.SetTag("llm.output_tokens", outputTokens);
        parentActivity?.SetTag("llm.total_tokens", totalTokens);
        parentActivity?.SetTag("llm.latency_ms", latencyMs);
        parentActivity?.SetTag("llm.cost_usd", cost);
    }

    private double EstimateCost(int tokens)
    {
        var costKey = _config.Llm.ModelCosts.Keys
            .FirstOrDefault(k => _modelName.Contains(k))
            ?? "default";

        var costPerMillion = _config.Llm.ModelCosts[costKey];
        return (tokens / 1_000_000.0) * costPerMillion;
    }

    private void LogPromptInfo(List<ChangedFileData> files)
    {
        var fileList = string.Join(", ", files.Take(_config.Processing.MaxFilesToShow)
            .Select(f => f.Filename));

        _logger.LogInformation("PR has {FileCount} files: {FileList}",
            files.Count, fileList);
    }

    private void LogAnalysisCompletion(AnalysisResult result)
    {
        _logger.LogInformation(
            "Analysis completed - ChangeUnits: {ChangeUnits}, Confidence: {Confidence:F2}",
            result.ChangeUnits.Count,
            result.ConfidenceScore);
    }
}