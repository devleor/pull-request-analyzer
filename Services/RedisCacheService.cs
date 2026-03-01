using System.Text.Json;
using StackExchange.Redis;

namespace PullRequestAnalyzer.Services;

public sealed class RedisCacheService
{
    private const string PrPrefix       = "cache:pr:";
    private const string AnalysisPrefix = "cache:analysis:";
    private const string JobPrefix      = "job:";

    private static readonly TimeSpan PrTtl       = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AnalysisTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan JobTtl      = TimeSpan.FromHours(24);

    private readonly IDatabase _db;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _db     = redis.GetDatabase();
        _logger = logger;
    }

    public Task<T?> GetPrAsync<T>(string owner, string repo, int number) =>
        GetAsync<T>($"{PrPrefix}{owner}/{repo}/{number}");

    public Task SetPrAsync<T>(string owner, string repo, int number, T value) =>
        SetAsync($"{PrPrefix}{owner}/{repo}/{number}", value, PrTtl);

    public Task<T?> GetAnalysisAsync<T>(string owner, string repo, int number) =>
        GetAsync<T>($"{AnalysisPrefix}{owner}/{repo}/{number}");

    public Task SetAnalysisAsync<T>(string owner, string repo, int number, T value) =>
        SetAsync($"{AnalysisPrefix}{owner}/{repo}/{number}", value, AnalysisTtl);

    public Task<T?> GetJobAsync<T>(string jobId) =>
        GetAsync<T>($"{JobPrefix}{jobId}");

    public Task SetJobAsync<T>(string jobId, T value) =>
        SetAsync($"{JobPrefix}{jobId}", value, JobTtl);

    public async Task<IEnumerable<string>> GetAllJobIdsAsync()
    {
        var server = _db.Multiplexer.GetServer(_db.Multiplexer.GetEndPoints().First());
        var ids    = new List<string>();

        await foreach (var key in server.KeysAsync(pattern: $"{JobPrefix}*"))
            ids.Add(key.ToString().Replace(JobPrefix, string.Empty));

        return ids;
    }

    public Task DeleteAsync(string key) => _db.KeyDeleteAsync(key);

    private async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var value = await _db.StringGetAsync(key);
            if (!value.HasValue) return default;

            _logger.LogDebug("Cache HIT: {Key}", key);
            return JsonSerializer.Deserialize<T>(value!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache GET failed: {Key}", key);
            return default;
        }
    }

    private async Task SetAsync<T>(string key, T value, TimeSpan ttl)
    {
        try
        {
            await _db.StringSetAsync(key, JsonSerializer.Serialize(value), ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache SET failed: {Key}", key);
        }
    }
}
