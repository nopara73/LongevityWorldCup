using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class DrainableOperationLifetimeTests
{
    [Fact]
    public async Task StopWaitsForEveryAdmittedOperationAndRejectsNewOnes()
    {
        var lifetime = new DrainableOperationLifetime("test owner");
        var first = lifetime.Enter();
        var second = lifetime.Enter();

        var drained = lifetime.StopAndDrainAsync();

        Assert.False(drained.IsCompleted);

        // A synchronous continuation of admitted work remains part of the same
        // operation tree even after shutdown closes admission.
        var nested = lifetime.Enter();

        Task<IDisposable?> unrelatedEntry;
        using (ExecutionContext.SuppressFlow())
            unrelatedEntry = Task.Run(lifetime.TryEnter);
        Assert.Null(await unrelatedEntry);

        nested.Dispose();
        Assert.False(drained.IsCompleted);

        second.Dispose();
        Assert.False(drained.IsCompleted);

        first.Dispose();
        await drained;

        Assert.Null(lifetime.TryEnter());
        Assert.Throws<ObjectDisposedException>(() => lifetime.Enter());
        second.Dispose();
        Assert.True(drained.IsCompletedSuccessfully);
        Assert.True(lifetime.StopAndDrainAsync().IsCompletedSuccessfully);
    }
}
