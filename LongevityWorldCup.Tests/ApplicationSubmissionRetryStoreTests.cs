using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ApplicationSubmissionRetryStoreTests
{
    [Fact]
    public async Task CompletedSubmission_IsReturnedForTheSamePayload()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new ApplicationSubmissionRetryStore(cache);
        var response = new ApplicationSubmissionResponse(true, true, "https://pay.example.test/invoice", "invoice-1");

        await using (var first = await store.AcquireAsync("submission-1", "fingerprint-1", CancellationToken.None))
        {
            Assert.True(first.ShouldProcess);
            first.Complete(response);
        }

        await using var retry = await store.AcquireAsync("submission-1", "fingerprint-1", CancellationToken.None);

        Assert.False(retry.ShouldProcess);
        Assert.False(retry.HasFingerprintConflict);
        Assert.Equal(response, retry.CachedResponse);
    }

    [Fact]
    public async Task CompletedSubmission_RejectsTheSameIdForDifferentPayload()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new ApplicationSubmissionRetryStore(cache);

        await using (var first = await store.AcquireAsync("submission-1", "fingerprint-1", CancellationToken.None))
        {
            first.Complete(new ApplicationSubmissionResponse(true, false, null, null));
        }

        await using var retry = await store.AcquireAsync("submission-1", "fingerprint-2", CancellationToken.None);

        Assert.False(retry.ShouldProcess);
        Assert.True(retry.HasFingerprintConflict);
        Assert.Null(retry.CachedResponse);
    }

    [Fact]
    public async Task ConcurrentRetry_WaitsAndThenReceivesCompletedResponse()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new ApplicationSubmissionRetryStore(cache);
        var response = new ApplicationSubmissionResponse(true, false, null, null);
        var first = await store.AcquireAsync("submission-1", "fingerprint-1", CancellationToken.None);

        var retryTask = store.AcquireAsync("submission-1", "fingerprint-1", CancellationToken.None).AsTask();
        Assert.False(retryTask.IsCompleted);

        first.Complete(response);
        await first.DisposeAsync();
        await using var retry = await retryTask;

        Assert.Equal(response, retry.CachedResponse);
    }
}
