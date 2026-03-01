using System.Text.Json;
using PullRequestAnalyzer.Messages;
using StackExchange.Redis;

namespace PullRequestAnalyzer.Services;

public sealed class RedisJobQueue
{
    private const string StreamKey  = "queue:analyze";
    private const string DlqKey     = "queue:analyze:dlq";
    private const string GroupName  = "pr-analyzers";
    private const int    BatchSize  = 10;

    private readonly string   _consumerName = $"worker-{Environment.MachineName}-{Guid.NewGuid():N}";
    private readonly IDatabase _db;
    private readonly ILogger<RedisJobQueue> _logger;

    public RedisJobQueue(IConnectionMultiplexer redis, ILogger<RedisJobQueue> logger)
    {
        _db     = redis.GetDatabase();
        _logger = logger;
        EnsureConsumerGroupAsync().GetAwaiter().GetResult();
    }

    public async Task<string> EnqueueAsync(AnalyzePullRequestCommand command)
    {
        var messageId = await _db.StreamAddAsync(
            StreamKey,
            [
                new("payload",     JsonSerializer.Serialize(command)),
                new("enqueued_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString())
            ]);

        _logger.LogInformation("Enqueued job {JobId} for PR#{Number} → {MessageId}",
            command.JobId, command.PullRequestData?.Number, messageId);

        return messageId!;
    }

    public async Task<IEnumerable<AnalyzePullRequestCommand>> DequeueAsync(CancellationToken ct)
    {
        var commands = new List<AnalyzePullRequestCommand>();

        try
        {
            var entries = await _db.StreamReadGroupAsync(
                StreamKey, GroupName, _consumerName, ">", count: BatchSize);

            foreach (var entry in entries)
            {
                var payloadField = entry.Values.FirstOrDefault(v => v.Name == "payload");
                if (payloadField.Value.IsNullOrEmpty) continue;

                var command = JsonSerializer.Deserialize<AnalyzePullRequestCommand>(payloadField.Value!);
                if (command is null) continue;

                command.StreamMessageId = entry.Id.ToString();
                commands.Add(command);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading from Redis Stream {Stream}", StreamKey);
        }

        return commands;
    }

    public Task AcknowledgeAsync(string messageId) =>
        _db.StreamAcknowledgeAsync(StreamKey, GroupName, messageId);

    public async Task MoveToDeadLetterAsync(string messageId, string reason)
    {
        await _db.StreamAddAsync(DlqKey,
        [
            new("original_id", messageId),
            new("reason",      reason),
            new("failed_at",   DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString())
        ]);

        await AcknowledgeAsync(messageId);

        _logger.LogWarning("Moved {MessageId} to DLQ. Reason: {Reason}", messageId, reason);
    }

    private async Task EnsureConsumerGroupAsync()
    {
        try
        {
            await _db.StreamCreateConsumerGroupAsync(
                StreamKey, GroupName, StreamPosition.NewMessages, createStream: true);

            _logger.LogInformation("Consumer group '{Group}' ready on '{Stream}'", GroupName, StreamKey);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            _logger.LogDebug("Consumer group '{Group}' already exists", GroupName);
        }
    }
}
