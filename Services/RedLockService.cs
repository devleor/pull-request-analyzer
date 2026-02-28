using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace PullRequestAnalyzer.Services
{
    /// <summary>
    /// Distributed lock service using the RedLock algorithm.
    ///
    /// RedLock guarantees mutual exclusion across multiple Redis nodes,
    /// preventing duplicate analysis of the same PR when multiple
    /// worker instances are running concurrently.
    ///
    /// Lock key pattern: lock:analyze:{owner}/{repo}/{number}
    /// </summary>
    public class RedLockService : IDisposable
    {
        private readonly RedLockFactory _factory;
        private readonly ILogger<RedLockService> _logger;

        private const string LOCK_PREFIX = "lock:analyze:";

        // How long the lock is held before auto-expiry (safety net)
        private static readonly TimeSpan LOCK_EXPIRY = TimeSpan.FromSeconds(60);

        // How long to wait trying to acquire the lock
        private static readonly TimeSpan LOCK_WAIT = TimeSpan.FromSeconds(5);

        // How often to retry acquiring the lock
        private static readonly TimeSpan LOCK_RETRY = TimeSpan.FromMilliseconds(200);

        public RedLockService(IConnectionMultiplexer redis, ILogger<RedLockService> logger)
        {
            _logger  = logger;
            _factory = RedLockFactory.Create(new List<RedLockMultiplexer>
            {
                new RedLockMultiplexer(redis)
            });
        }

        /// <summary>
        /// Acquires a distributed lock for a specific PR.
        /// Returns null if the lock could not be acquired (another worker is processing it).
        /// </summary>
        public async Task<IRedLock?> AcquireAnalysisLockAsync(string owner, string repo, int prNumber)
        {
            var resource = $"{LOCK_PREFIX}{owner}/{repo}/{prNumber}";

            var redLock = await _factory.CreateLockAsync(
                resource,
                LOCK_EXPIRY,
                LOCK_WAIT,
                LOCK_RETRY
            );

            if (redLock.IsAcquired)
            {
                _logger.LogInformation(
                    "Acquired RedLock for PR {Owner}/{Repo}#{Number} (resource: {Resource})",
                    owner, repo, prNumber, resource);
                return redLock;
            }

            _logger.LogWarning(
                "Could not acquire RedLock for PR {Owner}/{Repo}#{Number} — already being processed",
                owner, repo, prNumber);

            redLock.Dispose();
            return null;
        }

        /// <summary>
        /// Executes an action while holding a distributed lock.
        /// Returns false if the lock could not be acquired.
        /// </summary>
        public async Task<bool> ExecuteWithLockAsync(
            string owner,
            string repo,
            int prNumber,
            Func<Task> action)
        {
            await using var redLock = await AcquireAnalysisLockAsync(owner, repo, prNumber);

            if (redLock == null)
                return false;

            await action();
            return true;
        }

        public void Dispose()
        {
            _factory.Dispose();
        }
    }
}
