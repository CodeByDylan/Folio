using Folio.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Folio.Api.Tests;

public sealed class RefreshGateTests
{
    [Test]
    public async Task Callers_Arriving_During_A_Rebuild_Join_It()
    {
        RefreshGate gate = new();
        TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int rebuilds = 0;

        async Task<int> Rebuild(CancellationToken token)
        {
            _ = Interlocked.Increment(ref rebuilds);
            await held.Task;
            return rebuilds;
        }

        Task<int>[] callers =
            [.. Enumerable.Range(0, 4).Select(_ => gate.RunAsync(CancellationToken.None, Rebuild))];

        held.SetResult();
        int[] results = await Task.WhenAll(callers);

        await Assert.That(rebuilds).IsEqualTo(1);
        await Assert.That(results).IsEquivalentTo([1, 1, 1, 1]);
    }

    [Test]
    public async Task A_Rebuild_After_The_Last_One_Finished_Starts_Fresh()
    {
        RefreshGate gate = new();
        int rebuilds = 0;

        Task<int> Rebuild(CancellationToken token) => Task.FromResult(Interlocked.Increment(ref rebuilds));

        _ = await gate.RunAsync(CancellationToken.None, Rebuild);
        _ = await gate.RunAsync(CancellationToken.None, Rebuild);

        await Assert.That(rebuilds).IsEqualTo(2);
    }

    [Test]
    public async Task The_Gate_Is_Shared_By_Every_Request()
    {
        using FolioApp app = new(FolioApp.WorkedExample());
        _ = app.CreateClient();

        using IServiceScope one = app.Services.CreateScope();
        using IServiceScope two = app.Services.CreateScope();

        // A per-scope gate would join nothing, and only a timing-dependent test would notice.
        await Assert.That(one.ServiceProvider.GetRequiredService<RefreshGate>())
            .IsSameReferenceAs(two.ServiceProvider.GetRequiredService<RefreshGate>());
    }
}
