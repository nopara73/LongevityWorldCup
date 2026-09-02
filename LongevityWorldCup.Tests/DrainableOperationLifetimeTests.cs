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
        Assert.Null(lifetime.TryEnter());
        Assert.Throws<ObjectDisposedException>(() => lifetime.Enter());

        first.Dispose();
        Assert.False(drained.IsCompleted);

        second.Dispose();
        await drained;

        second.Dispose();
        Assert.True(drained.IsCompletedSuccessfully);
        Assert.True(lifetime.StopAndDrainAsync().IsCompletedSuccessfully);
    }
}
