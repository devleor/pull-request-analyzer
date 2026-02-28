using StackExchange.Redis;
using System.Text.Json;

namespace PullRequestAnalyzer.Services
{
    /// <summary>
    /// Centralized Redis cache service.
    /// Handles all cache operations: PR data, analysis results, and job status.
    /// </summary>
    public class RedisCacheService
    {
        private readonly IDatabase _db;
        private readonly ILogger<RedisCacheService> _logger;

        // Cache key prefixes
        private const string PR_PREFIX       = "cache:pr:";
        private const string ANALYSIS_PREFIX = "cache:analysis:";
        private const string JOB_PREFIX      = "job:";

        // Default TTLs
        private static readonly TimeSpan PR_TTL       = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ANALYSIS_TTL = TimeSpan.FromHours(1);
        private static readonly TimeSpan JOB_TTL      = TimeSpan.FromHours(24);

        public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
        {
            _db     = redis.GetDatabase();
            _logger = logger;
        }

        // -----------------------------------------------------------------------
        // PR Data Cache
        // -----------------------------------------------------------------------

        public async Task<T?> GetPrAsync<T>(string owner, string repo, int number)
        {
            var key = $"{PR_PREFIX}{owner}/{repo}/{number}";
            return await GetAsync<T>(key);
        }

        public async Task SetPrAsync<T>(string owner, string repo, int number, T value)
        {
            var key = $"{PR_PREFIX}{owner}/{repo}/{number}";
            await SetAsync(key, value, PR_TTL);
            _logger.LogInformation("Cached PR data for {Owner}/{Repo}#{Number} (TTL: {TTL})", owner, repo, number, PR_TTL);
        }

        // -----------------------------------------------------------------------
        // Analysis Results Cache
        // -----------------------------------------------------------------------

        public async Task<T?> GetAnalysisAsync<T>(string owner, string repo, int number)
        {
            var key = $"{ANALYSIS_PREFIX}{owner}/{repo}/{number}";
            return await GetAsync<T>(key);
        }

        public async Task SetAnalysisAsync<T>(string owner, string repo, int number, T value)
        {
            var key = $"{ANALYSIS_PREFIX}{owner}/{repo}/{number}";
            await SetAsync(key, value, ANALYSIS_TTL);
            _logger.LogInformation("Cached analysis for {Owner}/{Repo}#{Number} (TTL: {TTL})", owner, repo, number, ANALYSIS_TTL);
        }

        // -----------------------------------------------------------------------
        // Job Status Store
        // -----------------------------------------------------------------------

        public async Task<T?> GetJobAsync<T>(string jobId)
        {
            var key = $"{JOB_PREFIX}{jobId}";
            return await GetAsync<T>(key);
        }

        public async Task SetJobAsync<T>(string jobId, T value)
        {
            var key = $"{JOB_PREFIX}{jobId}";
            await SetAsync(key, value, JOB_TTL);
        }

        public async Task<IEnumerable<string>> GetAllJobIdsAsync()
        {
            // Scan for all job keys (use with care in production — prefer a secondary index)
            var server = _db.Multiplexer.GetServer(_db.Multiplexer.GetEndPoints().First());
            var keys   = server.KeysAsync(pattern: $"{JOB_PREFIX}*");
            var ids    = new List<string>();

            await foreach (var key in keys)
            {
                var id = key.ToString().Replace(JOB_PREFIX, "");
                ids.Add(id);
            }

            return ids;
        }

        // -----------------------------------------------------------------------
        // Generic Helpers
        // -----------------------------------------------------------------------

        private async Task<T?> GetAsync<T>(string key)
        {
            try
            {
                var value = await _db.StringGetAsync(key);
                if (!value.HasValue)
                    return default;

                _logger.LogDebug("Cache HIT: {Key}", key);
                return JsonSerializer.Deserialize<T>(value!);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache GET failed for key: {Key}", key);
                return default;
            }
        }

        private async Task SetAsync<T>(string key, T value, TimeSpan ttl)
        {
            try
            {
                var json = JsonSerializer.Serialize(value);
                await _db.StringSetAsync(key, json, ttl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache SET failed for key: {Key}", key);
            }
        }

        public async Task DeleteAsync(string key)
        {
            await _db.KeyDeleteAsync(key);
        }

        public async Task<bool> ExistsAsync(string key)
        {
            return await _db.KeyExistsAsync(key);
        }
    }
}
