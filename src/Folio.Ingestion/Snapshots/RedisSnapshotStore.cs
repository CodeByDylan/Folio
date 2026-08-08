using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Folio.Ingestion.Snapshots;

/// <summary>Keeps the last successful build's inputs in Redis.</summary>
public sealed class RedisSnapshotStore(
    IConnectionMultiplexer redis,
    string key,
    ILogger<RedisSnapshotStore> logger) : ISnapshotStore
{
    /// <inheritdoc />
    public async Task<StoredInputs?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            // StackExchange.Redis takes no token, so the wait is abandoned rather than the command.
            RedisValue value = await redis.GetDatabase().StringGetAsync(key).WaitAsync(cancellationToken);

            return value.IsNullOrEmpty ? null : StoredInputsSerializer.Deserialize((byte[])value!);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            SnapshotStoreLog.ReadFailed(logger, key, exception);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(StoredInputs inputs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        try
        {
            _ = await redis.GetDatabase()
                .StringSetAsync(key, StoredInputsSerializer.Serialize(inputs))
                .WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            SnapshotStoreLog.WriteFailed(logger, key, exception);
        }
    }
}
