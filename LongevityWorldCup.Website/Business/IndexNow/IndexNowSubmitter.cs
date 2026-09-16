using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace LongevityWorldCup.Website.Business.IndexNow;

public sealed class IndexNowSubmitter(
    IndexNowStateStore store,
    IHttpClientFactory clients,
    IOptions<IndexNowOptions> options,
    ILogger<IndexNowSubmitter> logger)
{
    public void Reconcile(IReadOnlyDictionary<string, string> content, DateTimeOffset now)
    {
        if (content.Any(pair => !IndexNowContentSnapshot.IsCanonicalUrl(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
            throw new InvalidDataException("IndexNow snapshots must contain only eligible canonical URLs and content hashes.");

        store.Update(state =>
        {
            foreach (var (url, hash) in content)
            {
                if (!state.Urls.TryGetValue(url, out var entry))
                    state.Urls[url] = entry = new IndexNowUrlState();
                if (entry.ContentHash == hash) continue;
                entry.ContentHash = hash;
                entry.ChangedAtUtc = now;
            }

            // A previously public URL must be notified even though it now returns 404/410.
            // Retaining its tombstone prevents repeated removal notifications on restart.
            foreach (var (url, entry) in state.Urls)
            {
                if (content.ContainsKey(url) || entry.ContentHash == IndexNowUrlState.Removed) continue;
                entry.ContentHash = IndexNowUrlState.Removed;
                entry.ChangedAtUtc = now;
            }

            if (state.RetryVersion != options.Value.RetryVersion)
            {
                state.RetryVersion = options.Value.RetryVersion;
                state.PausedReason = null;
                state.NotBeforeUtc = null;
                state.ConsecutiveFailures = 0;
            }
            state.LastScanUtc = now;
            return true;
        });
    }

    public async Task SubmitNextBatchAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = store.Read();
        if (state.PausedReason is not null || state.NotBeforeUtc > now) return;

        var batch = state.Urls
            .Where(pair => pair.Value.Pending &&
                (pair.Value.LastAttemptUtc is null || pair.Value.LastAttemptUtc <= now - IndexNowOptions.MinimumUrlInterval))
            .OrderBy(pair => pair.Value.LastAttemptUtc)
            .ThenBy(pair => pair.Value.ChangedAtUtc)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(Math.Clamp(options.Value.BatchSize, 1, 10_000))
            .ToDictionary(pair => pair.Key, pair => pair.Value.ContentHash, StringComparer.Ordinal);
        if (batch.Count == 0) return;

        // Validate again at the outbound boundary, including persisted tombstones.
        if (batch.Any(pair => !IndexNowContentSnapshot.IsCanonicalUrl(pair.Key) &&
            !(pair.Value == IndexNowUrlState.Removed && IndexNowContentSnapshot.IsCanonicalHostUrl(pair.Key))))
        {
            RecordResult(batch, now, null, "Invalid persisted URL; submission paused", permanent: true);
            return;
        }

        // Persist the attempt before network I/O. A crash/cancellation leaves the batch pending,
        // with the minimum interval preserved; remote acceptance and a local commit cannot be atomic.
        store.Update(current =>
        {
            foreach (var url in batch.Keys) current.Urls[url].LastAttemptUtc = now;
            current.LastSubmissionUtc = now;
            current.LastBatchSize = batch.Count;
            current.LastStatusCode = null;
            current.LastOutcome = "Sending";
            return true;
        });

        try
        {
            using var client = clients.CreateClient(nameof(IndexNowSubmitter));
            using var response = await client.PostAsJsonAsync(IndexNowOptions.Endpoint, new
            {
                host = new Uri(IndexNowContentSnapshot.SiteBaseUrl).Host,
                key = store.Key,
                keyLocation = $"{IndexNowContentSnapshot.SiteBaseUrl}/{store.Key}.txt",
                urlList = batch.Keys.ToArray()
            }, cancellationToken);

            var status = (int)response.StatusCode;
            var retryAfter = response.Headers.RetryAfter?.Date ??
                (response.Headers.RetryAfter?.Delta is { } delay ? now + delay : (DateTimeOffset?)null);
            switch (status)
            {
                case 200:
                    RecordResult(batch, now, status, "Submitted (indexing is not guaranteed)");
                    break;
                case 202:
                    RecordResult(batch, now, status, "Ownership validation pending", retryAfter: retryAfter);
                    break;
                case 408 or 429 or >= 500:
                    RecordResult(batch, now, status, "Transient failure; retry pending", retryAfter: retryAfter);
                    break;
                default:
                    RecordResult(batch, now, status, "Protocol error; submission paused", permanent: true);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            RecordResult(batch, now, null, "Transport failure; retry pending");
            logger.LogWarning("IndexNow transport failed ({FailureType}); pending work is retained.", ex.GetType().Name);
        }
    }

    private void RecordResult(IReadOnlyDictionary<string, string> batch, DateTimeOffset now,
        int? status, string outcome, bool permanent = false, DateTimeOffset? retryAfter = null)
    {
        store.Update(state =>
        {
            state.LastSubmissionUtc = now;
            state.LastStatusCode = status;
            state.LastOutcome = outcome;
            state.LastBatchSize = batch.Count;
            state.ConsecutiveFailures = status == 200 ? 0 : Math.Min(state.ConsecutiveFailures + 1, 20);
            state.PausedReason = permanent ? $"{outcome} (HTTP {status?.ToString() ?? "none"})" : null;
            if (status == 200)
                state.NotBeforeUtc = null;
            else
            {
                var baseMinutes = status == 202 ? 30 : 5;
                var minutes = Math.Min(1440, baseMinutes * Math.Pow(2, state.ConsecutiveFailures - 1));
                var backoff = now + TimeSpan.FromMinutes(minutes * (1 + Random.Shared.NextDouble() * 0.1));
                state.NotBeforeUtc = retryAfter > backoff ? retryAfter : backoff;
            }

            foreach (var (url, submittedHash) in batch)
            {
                var entry = state.Urls[url];
                entry.LastStatusCode = status;
                if (status != 200) continue;
                // If content changed during the request, that newer hash stays pending.
                entry.SubmittedHash = submittedHash;
                entry.SubmittedAtUtc = now;
            }
            return true;
        });

        if (permanent)
            logger.LogError("IndexNow paused: HTTP {Status}; {Count} URLs retained. Inspect indexnow-state.json and correct the error before changing IndexNow:RetryVersion.", status, batch.Count);
        else
            logger.LogInformation("IndexNow HTTP {Status}: {Outcome}; batch {Count}, next attempt no earlier than {NextAttemptUtc}.",
                status, outcome, batch.Count, store.Read().NotBeforeUtc);
    }
}
