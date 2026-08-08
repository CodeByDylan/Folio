using Folio.Api.Infrastructure;
using Loom.Results;
using Microsoft.Extensions.DependencyInjection;
using Slice = Folio.Api.Features.Refresh.TriggerRefresh;

namespace Folio.Api.Tests;

public sealed class RefreshGateTests
{
    [Test]
    public async Task Callers_Arriving_During_A_Rebuild_Join_It()
    {
        RefreshGate<int> gate = new();
        TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int rebuilds = 0;

        async Task<int> Rebuild()
        {
            _ = Interlocked.Increment(ref rebuilds);
            await held.Task;
            return rebuilds;
        }

        Task<int>[] callers =
            [.. Enumerable.Range(0, 4).Select(_ => gate.RunAsync(Rebuild))];

        held.SetResult();
        int[] results = await Task.WhenAll(callers);

        await Assert.That(rebuilds).IsEqualTo(1);
        await Assert.That(results).IsEquivalentTo([1, 1, 1, 1]);
    }

    [Test]
    public async Task A_Caller_Cancelling_Its_Wait_Does_Not_Cancel_The_Rebuild()
    {
        RefreshGate<int> gate = new();
        TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int completed = 0;

        async Task<int> Rebuild()
        {
            await held.Task;
            return Interlocked.Increment(ref completed);
        }

        using CancellationTokenSource leaving = new();
        Task<int> caller = gate.RunAsync(Rebuild).WaitAsync(leaving.Token);

        // The caller gives up waiting; the rebuild it started must still run to completion.
        leaving.Cancel();
        await Assert.That(async () => await caller).Throws<OperationCanceledException>();

        held.SetResult();
        int result = await gate.RunAsync(Rebuild);

        await Assert.That(result).IsEqualTo(1);
    }

    [Test]
    public async Task A_Rebuild_After_The_Last_One_Finished_Starts_Fresh()
    {
        RefreshGate<int> gate = new();
        int rebuilds = 0;

        Task<int> Rebuild() => Task.FromResult(Interlocked.Increment(ref rebuilds));

        _ = await gate.RunAsync(Rebuild);
        _ = await gate.RunAsync(Rebuild);

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
        await Assert.That(one.ServiceProvider.GetRequiredService<RefreshGate<Result<Slice.Response>>>())
            .IsSameReferenceAs(two.ServiceProvider.GetRequiredService<RefreshGate<Result<Slice.Response>>>());
    }
}
