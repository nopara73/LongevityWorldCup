using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ApplicationSubmissionRetryStoreTests
{
    [Fact]
    public async Task CompletedSubmission_IsReturnedForTheSamePayload()
    {
        using var fixture = new RetryStoreFixture();
        var store = fixture.CreateStore();
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
        using var fixture = new RetryStoreFixture();
        var store = fixture.CreateStore();

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
        using var fixture = new RetryStoreFixture();
        var store = fixture.CreateStore();
        var response = new ApplicationSubmissionResponse(true, false, null, null);
        var first = await store.AcquireAsync("submission-1", "fingerprint-1", CancellationToken.None);

        var retryTask = store.AcquireAsync("submission-1", "fingerprint-1", CancellationToken.None).AsTask();
        Assert.False(retryTask.IsCompleted);

        first.Complete(response);
        await first.DisposeAsync();
        await using var retry = await retryTask;

        Assert.Equal(response, retry.CachedResponse);
    }

    [Fact]
    public async Task CompletedSubmission_IsRecoveredAfterStoreRestart()
    {
        using var fixture = new RetryStoreFixture();
        var response = new ApplicationSubmissionResponse(true, true, "https://pay.example.test/invoice", "invoice-1");
        var firstStore = fixture.CreateStore();

        await using (var first = await firstStore.AcquireAsync("submission-1", "fingerprint-1", CancellationToken.None))
        {
            first.Complete(response);
        }

        var restartedStore = fixture.CreateStore();
        var recovered = await restartedStore.GetCompletedResponseAsync("submission-1", CancellationToken.None);
        await using var retry = await restartedStore.AcquireAsync("submission-1", "fingerprint-1", CancellationToken.None);

        Assert.Equal(response, recovered);
        Assert.False(retry.ShouldProcess);
        Assert.Equal(response, retry.CachedResponse);
    }

    [Fact]
    public async Task PersistedSubmission_RejectsTheSameIdForDifferentPayloadAfterRestart()
    {
        using var fixture = new RetryStoreFixture();
        var firstStore = fixture.CreateStore();

        await using (var first = await firstStore.AcquireAsync("submission-1", "fingerprint-1", CancellationToken.None))
        {
            first.Complete(new ApplicationSubmissionResponse(true, false, null, null));
        }

        var restartedStore = fixture.CreateStore();
        await using var retry = await restartedStore.AcquireAsync("submission-1", "fingerprint-2", CancellationToken.None);

        Assert.False(retry.ShouldProcess);
        Assert.True(retry.HasFingerprintConflict);
        Assert.Null(retry.CachedResponse);
    }

    [Fact]
    public async Task CancelledResponseRecovery_AllowsTheSameSubmissionToRecoverOnRetry()
    {
        using var fixture = new RetryStoreFixture();
        var firstStore = fixture.CreateStore();
        var response = new ApplicationSubmissionResponse(true, true, "https://pay.example.test/invoice", "invoice-1");

        await using (var first = await firstStore.AcquireAsync("submission-1", "fingerprint-1", CancellationToken.None))
        {
            first.Complete(response);
        }

        using var cancellation = new CancellationTokenSource();
        // The cold cache lookup occurs after acquiring the gate, just before reading the saved response.
        var restartedStore = fixture.CreateStore(cancellation.Cancel);
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            restartedStore.AcquireAsync("submission-1", "fingerprint-1", cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, exception.CancellationToken);

        using var retryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var retry = await restartedStore.AcquireAsync("submission-1", "fingerprint-1", retryTimeout.Token);

        Assert.False(retry.ShouldProcess);
        Assert.False(retry.HasFingerprintConflict);
        Assert.Equal(response, retry.CachedResponse);
    }

    [Fact]
    public async Task ResponseRecoveryException_AllowsTheSameSubmissionToRecoverOnRetry()
    {
        using var fixture = new RetryStoreFixture();
        var firstStore = fixture.CreateStore();
        var response = new ApplicationSubmissionResponse(true, true, "https://pay.example.test/invoice", "invoice-1");

        await using (var first = await firstStore.AcquireAsync("submission-1", "fingerprint-1", CancellationToken.None))
        {
            first.Complete(response);
        }

        var recoveryException = new InvalidOperationException("Response lookup failed.");
        var restartedStore = fixture.CreateStore(() => throw recoveryException);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            restartedStore.AcquireAsync("submission-1", "fingerprint-1", CancellationToken.None).AsTask());
        Assert.Same(recoveryException, exception);

        using var retryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var retry = await restartedStore.AcquireAsync("submission-1", "fingerprint-1", retryTimeout.Token);

        Assert.False(retry.ShouldProcess);
        Assert.False(retry.HasFingerprintConflict);
        Assert.Equal(response, retry.CachedResponse);
    }

    private sealed class FirstReadCallbackCache(IMemoryCache inner, Action beforeFirstRead) : IMemoryCache
    {
        private Action? _beforeFirstRead = beforeFirstRead;

        public bool TryGetValue(object key, out object? value)
        {
            Interlocked.Exchange(ref _beforeFirstRead, null)?.Invoke();
            return inner.TryGetValue(key, out value);
        }

        public ICacheEntry CreateEntry(object key) => inner.CreateEntry(key);
        public void Remove(object key) => inner.Remove(key);
        public void Dispose() => inner.Dispose();
    }

    private sealed class RetryStoreFixture : IDisposable
    {
        private readonly List<IMemoryCache> _caches = [];
        private readonly string _storageDirectory = Path.Combine(
            Path.GetTempPath(),
            "LongevityWorldCup.Tests",
            Guid.NewGuid().ToString("N"),
            "application-submission-responses");

        public ApplicationSubmissionRetryStore CreateStore(Action? beforeFirstCacheRead = null)
        {
            IMemoryCache cache = new MemoryCache(new MemoryCacheOptions());
            if (beforeFirstCacheRead is not null)
            {
                cache = new FirstReadCallbackCache(cache, beforeFirstCacheRead);
            }

            _caches.Add(cache);
            return new ApplicationSubmissionRetryStore(cache, _storageDirectory, TimeProvider.System);
        }

        public void Dispose()
        {
            foreach (var cache in _caches)
            {
                cache.Dispose();
            }

            var root = Directory.GetParent(_storageDirectory)?.FullName;
            if (root is not null && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
