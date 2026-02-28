using StackExchange.Redis;
using System.Text.Json;
using PullRequestAnalyzer.Messages;

namespace PullRequestAnalyzer.Services
{
    /// <summary>
    /// Redis Streams-based job queue for PR analysis commands.
    /// Uses Redis Streams with consumer groups for reliable, at-least-once delivery.
    ///
    /// Stream key: queue:analyze
    /// Consumer group: pr-analyzers
    /// </summary>
    public class RedisJobQueue
    {
        private readonly IDatabase _db;
        private readonly ILogger<RedisJobQueue> _logger;

        private const string STREAM_KEY    = "queue:analyze";
        private const string GROUP_NAME    = "pr-analyzers";
        private const string CONSUMER_NAME = "worker-1";
        private const int    BATCH_SIZE    = 10;

        public RedisJobQueue(IConnectionMultiplexer redis, ILogger<RedisJobQueue> logger)
        {
            _db     = redis.GetDatabase();
            _logger = logger;
            EnsureConsumerGroupAsync().GetAwaiter().GetResult();
        }

        // -----------------------------------------------------------------------
        // Producer: Enqueue a command
        // -----------------------------------------------------------------------

        public async Task<string> EnqueueAsync(AnalyzePullRequestCommand command)
        {
            var payload = JsonSerializer.Serialize(command);

            var messageId = await _db.StreamAddAsync(
                STREAM_KEY,
                new NameValueEntry[]
                {
                    new("payload", payload),
                    new("enqueued_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString())
                }
            );

            _logger.LogInformation(
                "Enqueued analysis job {JobId} for PR#{Number} → stream message {MessageId}",
                command.JobId, command.PullRequestData?.Number, messageId);

            return messageId!;
        }

        // -----------------------------------------------------------------------
        // Consumer: Read pending messages
        // -----------------------------------------------------------------------

        public async Task<IEnumerable<AnalyzePullRequestCommand>> DequeueAsync(CancellationToken ct)
        {
            var commands = new List<AnalyzePullRequestCommand>();

            try
            {
                // Read new messages from the stream (">") means only undelivered
                var entries = await _db.StreamReadGroupAsync(
                    STREAM_KEY,
                    GROUP_NAME,
                    CONSUMER_NAME,
                    ">",
                    count: BATCH_SIZE
                );

                foreach (var entry in entries)
                {
                    var payloadField = entry.Values.FirstOrDefault(v => v.Name == "payload");
                    if (payloadField.Value.IsNullOrEmpty) continue;

                    var command = JsonSerializer.Deserialize<AnalyzePullRequestCommand>(payloadField.Value!);
                    if (command != null)
                    {
                        command.StreamMessageId = entry.Id.ToString();
                        commands.Add(command);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading from Redis Stream {Stream}", STREAM_KEY);
            }

            return commands;
        }

        // -----------------------------------------------------------------------
        // Acknowledge: Mark message as processed
        // -----------------------------------------------------------------------

        public async Task AcknowledgeAsync(string streamMessageId)
        {
            await _db.StreamAcknowledgeAsync(STREAM_KEY, GROUP_NAME, streamMessageId);
            _logger.LogDebug("Acknowledged stream message {MessageId}", streamMessageId);
        }

        // -----------------------------------------------------------------------
        // Dead Letter: Move failed messages to a dead-letter stream
        // -----------------------------------------------------------------------

        public async Task MoveToDeadLetterAsync(string streamMessageId, string reason)
        {
            const string DLQ_KEY = "queue:analyze:dlq";

            await _db.StreamAddAsync(
                DLQ_KEY,
                new NameValueEntry[]
                {
                    new("original_id", streamMessageId),
                    new("reason", reason),
                    new("failed_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString())
                }
            );

            // Acknowledge original to remove from pending
            await AcknowledgeAsync(streamMessageId);

            _logger.LogWarning(
                "Moved message {MessageId} to dead-letter queue. Reason: {Reason}",
                streamMessageId, reason);
        }

        // -----------------------------------------------------------------------
        // Queue Info
        // -----------------------------------------------------------------------

        public async Task<long> GetQueueLengthAsync()
        {
            return await _db.StreamLengthAsync(STREAM_KEY);
        }

        public async Task<long> GetPendingCountAsync()
        {
            var info = await _db.StreamPendingAsync(STREAM_KEY, GROUP_NAME);
            return info.PendingMessageCount;
        }

        // -----------------------------------------------------------------------
        // Setup
        // -----------------------------------------------------------------------

        private async Task EnsureConsumerGroupAsync()
        {
            try
            {
                await _db.StreamCreateConsumerGroupAsync(
                    STREAM_KEY,
                    GROUP_NAME,
                    StreamPosition.NewMessages,
                    createStream: true
                );
                _logger.LogInformation("Redis Stream consumer group '{Group}' ready on '{Stream}'", GROUP_NAME, STREAM_KEY);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
            {
                // Group already exists — that's fine
                _logger.LogDebug("Consumer group '{Group}' already exists on '{Stream}'", GROUP_NAME, STREAM_KEY);
            }
        }
    }
}
