namespace Folio.Api.Infrastructure;

/// <summary>Runs one rebuild at a time, joining callers that arrive while one is running.</summary>
internal sealed class RefreshGate
{
    private readonly Lock _gate = new();

    private Task? _running;

    /// <summary>Starts a rebuild, or returns the one already in flight; the caller awaits it under its own token.</summary>
    /// <typeparam name="T">What a rebuild produces.</typeparam>
    /// <param name="rebuild">Starts a rebuild, governed by its own token, not the caller's.</param>
    /// <returns>The result of this rebuild or of the one it joined.</returns>
    public Task<T> RunAsync<T>(Func<Task<T>> rebuild)
    {
        ArgumentNullException.ThrowIfNull(rebuild);

        lock (_gate)
        {
            if (_running is Task<T> { IsCompleted: false } inflight)
            {
                return inflight;
            }

            Task<T> started = rebuild();
            _running = started;

            return started;
        }
    }
}
