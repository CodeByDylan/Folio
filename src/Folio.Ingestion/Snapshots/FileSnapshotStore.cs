using Microsoft.Extensions.Logging;

namespace Folio.Ingestion.Snapshots;

/// <summary>Keeps the last successful build's inputs in a local file.</summary>
public sealed class FileSnapshotStore(string path, ILogger<FileSnapshotStore> logger) : ISnapshotStore
{
    /// <inheritdoc />
    public async Task<StoredInputs?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return StoredInputsSerializer.Deserialize(await File.ReadAllBytesAsync(path, cancellationToken));
        }
        catch (IOException exception)
        {
            SnapshotStoreLog.ReadFailed(logger, path, exception);
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            SnapshotStoreLog.ReadFailed(logger, path, exception);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(StoredInputs inputs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string temporary = path + ".tmp";

        try
        {
            _ = Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(temporary, StoredInputsSerializer.Serialize(inputs), cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SnapshotStoreLog.WriteFailed(logger, path, exception);
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
