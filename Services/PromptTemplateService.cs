using System.Text.Json;
using StackExchange.Redis;
using Microsoft.SemanticKernel;

namespace PullRequestAnalyzer.Services;

public interface IPromptTemplateService
{
    Task<string?> GetPromptTemplateAsync(string key, string? version = null);
    Task SavePromptTemplateAsync(string key, string template, string? version = null, Dictionary<string, object>? metadata = null);
    Task<List<string>> GetPromptVersionsAsync(string key);
    Task<Dictionary<string, string>> GetAllPromptTemplatesAsync();
}

public class PromptTemplateService : IPromptTemplateService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PromptTemplateService> _logger;
    private const string PROMPT_PREFIX = "prompt:";
    private const string PROMPT_VERSION_PREFIX = "prompt:version:";
    private const int DEFAULT_TTL_DAYS = 90;

    public PromptTemplateService(
        IConnectionMultiplexer redis,
        ILogger<PromptTemplateService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<string?> GetPromptTemplateAsync(string key, string? version = null)
    {
        try
        {
            var db = _redis.GetDatabase();
            var redisKey = version != null
                ? $"{PROMPT_VERSION_PREFIX}{key}:{version}"
                : $"{PROMPT_PREFIX}{key}";

            var value = await db.StringGetAsync(redisKey);

            if (value.IsNullOrEmpty)
            {
                _logger.LogWarning("Prompt template not found for key: {Key}, version: {Version}", key, version ?? "latest");
                return null;
            }

            _logger.LogDebug("Retrieved prompt template for key: {Key}, version: {Version}", key, version ?? "latest");

            // Try to deserialize as PromptData, otherwise return as string
            try
            {
                var promptData = JsonSerializer.Deserialize<PromptData>(value!);
                return promptData?.Template;
            }
            catch
            {
                // If it's not JSON, return as plain string (backward compatibility)
                return value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve prompt template for key: {Key}", key);
            throw;
        }
    }

    public async Task SavePromptTemplateAsync(string key, string template, string? version = null, Dictionary<string, object>? metadata = null)
    {
        try
        {
            var db = _redis.GetDatabase();

            // Determine if this is a system prompt (never expires)
            var isSystemPrompt = metadata?.ContainsKey("type") == true &&
                                metadata["type"]?.ToString() == "system";

            TimeSpan? expiry = isSystemPrompt ? null : TimeSpan.FromDays(DEFAULT_TTL_DAYS);

            // Save with version if specified
            if (version != null)
            {
                var versionKey = $"{PROMPT_VERSION_PREFIX}{key}:{version}";
                var promptData = new PromptData
                {
                    Template = template,
                    Version = version,
                    CreatedAt = DateTime.UtcNow,
                    Metadata = metadata
                };

                await db.StringSetAsync(versionKey, JsonSerializer.Serialize(promptData), expiry, When.Always);

                // Track version in a set
                await db.SetAddAsync($"{PROMPT_PREFIX}{key}:versions", version);

                _logger.LogInformation("Saved prompt template version: {Key}:{Version}, Expires: {Expires}",
                    key, version, expiry?.TotalDays ?? -1);
            }

            // Always save/update the latest version
            var latestKey = $"{PROMPT_PREFIX}{key}";
            var latestData = new PromptData
            {
                Template = template,
                Version = version ?? "latest",
                CreatedAt = DateTime.UtcNow,
                Metadata = metadata
            };

            await db.StringSetAsync(latestKey, JsonSerializer.Serialize(latestData), expiry, When.Always);

            _logger.LogInformation("Saved prompt template: {Key} (latest), Type: {Type}, Expires: {Expires}days",
                key, metadata?.GetValueOrDefault("type") ?? "user", expiry?.TotalDays ?? -1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save prompt template for key: {Key}", key);
            throw;
        }
    }

    public async Task<List<string>> GetPromptVersionsAsync(string key)
    {
        try
        {
            var db = _redis.GetDatabase();
            var versions = await db.SetMembersAsync($"{PROMPT_PREFIX}{key}:versions");

            return versions
                .Where(v => v.HasValue)
                .Select(v => v.ToString())
                .OrderByDescending(v => v)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get prompt versions for key: {Key}", key);
            throw;
        }
    }

    public async Task<Dictionary<string, string>> GetAllPromptTemplatesAsync()
    {
        try
        {
            var server = _redis.GetServers().First();
            var db = _redis.GetDatabase();
            var templates = new Dictionary<string, string>();

            // Get all prompt keys (excluding versions)
            var keys = server.Keys(pattern: $"{PROMPT_PREFIX}*")
                .Where(k => !k.ToString().Contains(":version:") && !k.ToString().Contains(":versions"));

            foreach (var key in keys)
            {
                var value = await db.StringGetAsync(key);
                if (!value.IsNullOrEmpty)
                {
                    var cleanKey = key.ToString().Replace(PROMPT_PREFIX, "");

                    try
                    {
                        var promptData = JsonSerializer.Deserialize<PromptData>(value!);
                        templates[cleanKey] = promptData?.Template ?? value.ToString();
                    }
                    catch
                    {
                        templates[cleanKey] = value.ToString();
                    }
                }
            }

            _logger.LogDebug("Retrieved {Count} prompt templates from Redis", templates.Count);
            return templates;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all prompt templates");
            throw;
        }
    }

    private class PromptData
    {
        public string Template { get; set; } = string.Empty;
        public string Version { get; set; } = "latest";
        public DateTime CreatedAt { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}

/// <summary>
/// Extension methods for Semantic Kernel integration
/// </summary>
public static class PromptTemplateExtensions
{
    /// <summary>
    /// Creates a Semantic Kernel prompt template config from a stored template
    /// </summary>
    public static PromptTemplateConfig ToPromptTemplateConfig(this string template, string? description = null)
    {
        return new PromptTemplateConfig(template)
        {
            Description = description ?? "Pull Request Analysis Prompt",
            InputVariables = new List<InputVariable>
            {
                new() { Name = "pr_title", Description = "Pull request title" },
                new() { Name = "pr_description", Description = "Pull request description" },
                new() { Name = "pr_author", Description = "Pull request author" },
                new() { Name = "commits", Description = "Commit messages" },
                new() { Name = "files_changed", Description = "Files changed with diffs" }
            }
        };
    }

    /// <summary>
    /// Load and render a prompt template from Redis
    /// </summary>
    public static async Task<string> RenderPromptFromTemplateAsync(
        this IPromptTemplateService templateService,
        Kernel kernel,
        string templateKey,
        Dictionary<string, object> variables,
        string? version = null)
    {
        var template = await templateService.GetPromptTemplateAsync(templateKey, version);
        if (template == null)
        {
            throw new InvalidOperationException($"Prompt template '{templateKey}' not found");
        }

        var config = template.ToPromptTemplateConfig();
        var promptTemplate = kernel.CreateFunctionFromPrompt(config);

        var arguments = new KernelArguments();
        foreach (var kvp in variables)
        {
            arguments[kvp.Key] = kvp.Value;
        }

        var result = await kernel.InvokeAsync(promptTemplate, arguments);
        return result.ToString();
    }
}