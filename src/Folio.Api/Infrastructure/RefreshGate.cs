namespace Folio.Api.Infrastructure;

/// <summary>Runs one rebuild at a time, joining callers that arrive while one is running.</summary>
internal sealed class RefreshGate
{
    private readonly Lock _gate = new();

    private Task? _running;

    /// <summary>Runs a rebuild under the starter's token, or awaits the one already in flight.</summary>
    /// <typeparam name="T">What a rebuild produces.</typeparam>
    /// <param name="cancellationToken">Governs the run only when this caller starts it.</param>
    /// <param name="rebuild">Starts a rebuild.</param>
    /// <returns>The result of this rebuild or of the one it joined.</returns>
    public Task<T> RunAsync<T>(CancellationToken cancellationToken, Func<CancellationToken, Task<T>> rebuild)
    {
        ArgumentNullException.ThrowIfNull(rebuild);

        lock (_gate)
        {
            if (_running is Task<T> { IsCompleted: false } inflight)
            {
                return inflight;
            }

            Task<T> started = rebuild(cancellationToken);
            _running = started;

            return started;
        }
    }
}
