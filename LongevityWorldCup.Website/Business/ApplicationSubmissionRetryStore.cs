using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace LongevityWorldCup.Website.Business;

public sealed record ApplicationSubmissionResponse(
    bool Success,
    bool PaymentRequired,
    string? CheckoutLink,
    string? InvoiceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? PaymentUnavailable = null);

public sealed class ApplicationSubmissionRetryStore(IMemoryCache cache)
{
    private static readonly TimeSpan CompletedSubmissionLifetime = TimeSpan.FromHours(1);
    private const string CacheKeyPrefix = "application-submission:";

    private readonly IMemoryCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly ConcurrentDictionary<string, SubmissionLock> _locks = new(StringComparer.Ordinal);

    public async ValueTask<Lease> AcquireAsync(
        string submissionId,
        string requestFingerprint,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(submissionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);

        var state = AcquireState(submissionId);
        try
        {
            await state.Gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            ReleaseReference(submissionId, state);
            throw;
        }

        var hasCompletedSubmission = _cache.TryGetValue<CompletedSubmission>(
            CacheKeyPrefix + submissionId,
            out var completedSubmission);
        var fingerprintMatches = hasCompletedSubmission
            && string.Equals(completedSubmission!.RequestFingerprint, requestFingerprint, StringComparison.Ordinal);

        return new Lease(
            this,
            submissionId,
            requestFingerprint,
            state,
            fingerprintMatches ? completedSubmission!.Response : null,
            hasCompletedSubmission && !fingerprintMatches);
    }

    private SubmissionLock AcquireState(string submissionId)
    {
        while (true)
        {
            var state = _locks.GetOrAdd(submissionId, static _ => new SubmissionLock());
            lock (state.Sync)
            {
                if (state.Removed)
                {
                    continue;
                }

                state.Users++;
                return state;
            }
        }
    }

    private void Complete(string submissionId, string requestFingerprint, ApplicationSubmissionResponse response)
    {
        _cache.Set(
            CacheKeyPrefix + submissionId,
            new CompletedSubmission(requestFingerprint, response),
            CompletedSubmissionLifetime);
    }

    private void Release(string submissionId, SubmissionLock state)
    {
        state.Gate.Release();
        ReleaseReference(submissionId, state);
    }

    private void ReleaseReference(string submissionId, SubmissionLock state)
    {
        lock (state.Sync)
        {
            state.Users--;
            if (state.Users != 0)
            {
                return;
            }

            state.Removed = true;
            _locks.TryRemove(new KeyValuePair<string, SubmissionLock>(submissionId, state));
        }
    }

    private sealed record CompletedSubmission(
        string RequestFingerprint,
        ApplicationSubmissionResponse Response);

    internal sealed class SubmissionLock
    {
        public object Sync { get; } = new();
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int Users { get; set; }
        public bool Removed { get; set; }
    }

    public sealed class Lease : IAsyncDisposable, IDisposable
    {
        private readonly ApplicationSubmissionRetryStore _owner;
        private readonly string _submissionId;
        private readonly string _requestFingerprint;
        private readonly SubmissionLock _state;
        private int _disposed;

        internal Lease(
            ApplicationSubmissionRetryStore owner,
            string submissionId,
            string requestFingerprint,
            SubmissionLock state,
            ApplicationSubmissionResponse? cachedResponse,
            bool hasFingerprintConflict)
        {
            _owner = owner;
            _submissionId = submissionId;
            _requestFingerprint = requestFingerprint;
            _state = state;
            CachedResponse = cachedResponse;
            HasFingerprintConflict = hasFingerprintConflict;
        }

        public ApplicationSubmissionResponse? CachedResponse { get; }
        public bool HasFingerprintConflict { get; }
        public bool ShouldProcess => CachedResponse is null && !HasFingerprintConflict;

        public void Complete(ApplicationSubmissionResponse response)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (!ShouldProcess)
            {
                throw new InvalidOperationException("Only a new application submission can be completed.");
            }

            _owner.Complete(_submissionId, _requestFingerprint, response);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Release(_submissionId, _state);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
