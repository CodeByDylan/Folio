namespace Folio.Api.Infrastructure;

/// <summary>Runs one rebuild at a time, joining callers that arrive while one is running.</summary>
/// <typeparam name="T">What a rebuild produces; fixed per gate so every caller joins the same one.</typeparam>
internal sealed class RefreshGate<T>
{
    private readonly Lock _gate = new();

    private Task<T>? _running;

    /// <summary>Starts a rebuild, or returns the one already in flight; the caller awaits it under its own token.</summary>
    /// <param name="rebuild">Starts a rebuild, governed by its own token, not the caller's.</param>
    /// <returns>The result of this rebuild or of the one it joined.</returns>
    public Task<T> RunAsync(Func<Task<T>> rebuild)
    {
        ArgumentNullException.ThrowIfNull(rebuild);

        lock (_gate)
        {
            if (_running is { IsCompleted: false } inflight)
            {
                return inflight;
            }

            Task<T> started = rebuild();
            _running = started;

            return started;
        }
    }
}
