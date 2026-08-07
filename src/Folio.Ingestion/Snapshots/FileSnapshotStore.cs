using Microsoft.Extensions.Logging;

namespace Folio.Ingestion.Snapshots;

/// <summary>Keeps the last successful build's inputs in a local file.</summary>
public sealed class FileSnapshotStore(string path, ILogger<FileSnapshotStore> logger) : ISnapshotStore
{
    // A relative path resolves against the application base, not the launch directory, so the durable
    // copy lands in the same place however the process was started.
    private readonly string _path = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    /// <inheritdoc />
    public async Task<StoredInputs?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            return StoredInputsSerializer.Deserialize(await File.ReadAllBytesAsync(_path, cancellationToken));
        }
        catch (IOException exception)
        {
            SnapshotStoreLog.ReadFailed(logger, _path, exception);
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            SnapshotStoreLog.ReadFailed(logger, _path, exception);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(StoredInputs inputs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        string directory = Path.GetDirectoryName(_path)!;
        string temporary = _path + ".tmp";

        try
        {
            _ = Directory.CreateDirectory(directory);

            // Flush to disk before the rename, so a power loss cannot leave the file present but empty.
            await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(StoredInputsSerializer.Serialize(inputs), cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SnapshotStoreLog.WriteFailed(logger, _path, exception);
        }
    }
}

internal static partial class SnapshotStoreLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read stored inputs from {Path}.")]
    public static partial void ReadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not store inputs at {Path}; the next refresh refetches.")]
    public static partial void WriteFailed(ILogger logger, string path, Exception exception);
}
