using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongevityWorldCup.Website.Tools;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace LongevityWorldCup.Website.Business;

public sealed record ApplicationSubmissionResponse(
    bool Success,
    bool PaymentRequired,
    string? CheckoutLink,
    string? InvoiceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? PaymentUnavailable = null);

public sealed class ApplicationSubmissionRetryStore
{
    internal static readonly TimeSpan CompletedSubmissionLifetime =
        TimeSpan.FromMinutes(BtcpayInvoiceClient.MaximumInvoiceExpirationMinutes);
    private const string CacheKeyPrefix = "application-submission:";

    private readonly IMemoryCache _cache;
    private readonly string _storageDirectory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ApplicationSubmissionRetryStore> _logger;
    private readonly ConcurrentDictionary<string, SubmissionLock> _locks = new(StringComparer.Ordinal);

    public ApplicationSubmissionRetryStore(
        IMemoryCache cache,
        ILogger<ApplicationSubmissionRetryStore> logger)
        : this(
            cache,
            Path.Combine(EnvironmentHelpers.GetDataDir(), "ApplicationSubmissionResponses"),
            TimeProvider.System,
            logger)
    {
    }

    public ApplicationSubmissionRetryStore(IMemoryCache cache)
        : this(
            cache,
            Path.Combine(EnvironmentHelpers.GetDataDir(), "ApplicationSubmissionResponses"),
            TimeProvider.System,
            NullLogger<ApplicationSubmissionRetryStore>.Instance)
    {
    }

    internal ApplicationSubmissionRetryStore(
        IMemoryCache cache,
        string storageDirectory,
        TimeProvider timeProvider,
        ILogger<ApplicationSubmissionRetryStore>? logger = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _storageDirectory = string.IsNullOrWhiteSpace(storageDirectory)
            ? throw new ArgumentException("A submission response storage directory is required.", nameof(storageDirectory))
            : Path.GetFullPath(storageDirectory);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? NullLogger<ApplicationSubmissionRetryStore>.Instance;
    }

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

        var completedSubmission = await GetCompletedSubmissionAsync(submissionId, ct).ConfigureAwait(false);
        var fingerprintMatches = completedSubmission is not null
            && string.Equals(completedSubmission.RequestFingerprint, requestFingerprint, StringComparison.Ordinal);

        return new Lease(
            this,
            submissionId,
            requestFingerprint,
            state,
            fingerprintMatches ? completedSubmission!.Response : null,
            completedSubmission is not null && !fingerprintMatches);
    }

    public async ValueTask<ApplicationSubmissionResponse?> GetCompletedResponseAsync(
        string submissionId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(submissionId);
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

        try
        {
            var completedSubmission = await GetCompletedSubmissionAsync(submissionId, ct).ConfigureAwait(false);
            return completedSubmission?.Response;
        }
        finally
        {
            Release(submissionId, state);
        }
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

    private async ValueTask<CompletedSubmission?> GetCompletedSubmissionAsync(
        string submissionId,
        CancellationToken ct)
    {
        var cacheKey = CacheKeyPrefix + submissionId;
        if (_cache.TryGetValue<CompletedSubmission>(cacheKey, out var cachedSubmission))
        {
            if (cachedSubmission!.ExpiresAtUtc > _timeProvider.GetUtcNow())
            {
                return cachedSubmission;
            }

            _cache.Remove(cacheKey);
        }

        var path = GetStoragePath(submissionId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            var completedSubmission = await JsonSerializer
                .DeserializeAsync<CompletedSubmission>(stream, cancellationToken: ct)
                .ConfigureAwait(false);

            if (completedSubmission is null || completedSubmission.ExpiresAtUtc <= _timeProvider.GetUtcNow())
            {
                TryDeleteExpiredSubmission(path);
                return null;
            }

            _cache.Set(cacheKey, completedSubmission, completedSubmission.ExpiresAtUtc);
            return completedSubmission;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to read a persisted application submission response. SubmissionId={SubmissionId}",
                submissionId);
            return null;
        }
    }

    private void Complete(string submissionId, string requestFingerprint, ApplicationSubmissionResponse response)
    {
        var completedSubmission = new CompletedSubmission(
            requestFingerprint,
            response,
            _timeProvider.GetUtcNow().Add(CompletedSubmissionLifetime));
        _cache.Set(
            CacheKeyPrefix + submissionId,
            completedSubmission,
            completedSubmission.ExpiresAtUtc);

        try
        {
            PersistCompletedSubmission(submissionId, completedSubmission);
        }
        catch (Exception ex)
        {
            // Persistence must not turn an already accepted application and invoice
            // into a client-visible submission failure. Memory recovery still works.
            _logger.LogWarning(
                ex,
                "Failed to persist an application submission response. SubmissionId={SubmissionId}",
                submissionId);
        }
    }

    private void PersistCompletedSubmission(string submissionId, CompletedSubmission completedSubmission)
    {
        Directory.CreateDirectory(_storageDirectory);
        TryDeleteExpiredSubmissions();
        var destinationPath = GetStoragePath(submissionId);
        var temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(completedSubmission);
            File.WriteAllBytes(temporaryPath, json);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string GetStoragePath(string submissionId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(submissionId));
        return Path.Combine(_storageDirectory, Convert.ToHexString(hash).ToLowerInvariant() + ".json");
    }

    private void TryDeleteExpiredSubmission(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete an expired application submission response at {Path}", path);
        }
    }

    private void TryDeleteExpiredSubmissions()
    {
        try
        {
            var oldestRetainedWriteTime = _timeProvider.GetUtcNow().Subtract(CompletedSubmissionLifetime).UtcDateTime;
            foreach (var path in Directory.EnumerateFiles(_storageDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) <= oldestRetainedWriteTime)
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to inspect or delete an expired application submission response at {Path}", path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to prune expired application submission responses.");
        }
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
        ApplicationSubmissionResponse Response,
        DateTimeOffset ExpiresAtUtc);

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
