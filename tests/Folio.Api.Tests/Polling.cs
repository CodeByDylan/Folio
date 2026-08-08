namespace Folio.Api.Tests;

/// <summary>Waits for an asynchronous condition, failing loudly rather than hanging a test.</summary>
internal static class Polling
{
    /// <summary>Polls until the condition holds or ten seconds pass.</summary>
    /// <param name="settled">The condition to wait for.</param>
    /// <returns>A task faulting with <see cref="TimeoutException" /> if the condition never holds.</returns>
    public static async Task Until(Func<Task<bool>> settled)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            if (await settled())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The condition did not hold within the deadline.");
    }

    /// <summary>Polls a synchronous condition until it holds or ten seconds pass.</summary>
    /// <param name="settled">The condition to wait for.</param>
    /// <returns>A task faulting with <see cref="TimeoutException" /> if the condition never holds.</returns>
    public static Task Until(Func<bool> settled) => Until(() => Task.FromResult(settled()));
}
