using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace PullRequestAnalyzer.Services;

public sealed class RedLockService : IDisposable
{
    private const string LockPrefix = "lock:analyze:";

    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Wait   = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Retry  = TimeSpan.FromMilliseconds(200);

    private readonly RedLockFactory _factory;
    private readonly ILogger<RedLockService> _logger;

    public RedLockService(IConnectionMultiplexer redis, ILogger<RedLockService> logger)
    {
        _logger  = logger;
        _factory = RedLockFactory.Create([new RedLockMultiplexer(redis)]);
    }

    public async Task<IRedLock?> AcquireAsync(string owner, string repo, int prNumber)
    {
        var resource = $"{LockPrefix}{owner}/{repo}/{prNumber}";
        var redLock  = await _factory.CreateLockAsync(resource, Expiry, Wait, Retry);

        if (redLock.IsAcquired)
        {
            _logger.LogInformation("Acquired lock for PR {Owner}/{Repo}#{Number}", owner, repo, prNumber);
            return redLock;
        }

        _logger.LogWarning("Could not acquire lock for PR {Owner}/{Repo}#{Number} — already processing", owner, repo, prNumber);
        redLock.Dispose();
        return null;
    }

    public async Task<bool> ExecuteWithLockAsync(string owner, string repo, int prNumber, Func<Task> action)
    {
        await using var redLock = await AcquireAsync(owner, repo, prNumber);
        if (redLock is null) return false;

        await action();
        return true;
    }

    public void Dispose() => _factory.Dispose();
}
