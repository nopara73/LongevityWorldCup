using System.Net;
using System.Text.Json;

namespace LongevityWorldCup.Website.Business;

public class ThreadsApiClient
{
    private const string CreateThreadEndpoint = "https://graph.threads.net/me/threads";
    private const string PublishThreadEndpoint = "https://graph.threads.net/me/threads_publish";
    private const string RefreshAccessTokenEndpoint = "https://graph.threads.net/refresh_access_token";
    private const string ContainerFields = "id,status,error_message";
    private const int MaxTextLength = 500;
    private static readonly TimeSpan ProactiveRefreshWindow = TimeSpan.FromDays(14);
    private static readonly TimeSpan UnknownExpiryRefreshRetryInterval = TimeSpan.FromHours(20);
    private static readonly int[] ContainerReadyPollDelaysMs = [1000, 2000, 3000, 5000, 5000, 10000, 10000, 15000];
    private static readonly int[] PublishRetryDelaysMs = [1000, 3000, 7000];

    private readonly HttpClient _http;
    private readonly Config _config;
    private readonly ILogger<ThreadsApiClient> _log;
    private readonly object _tokenLock = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _accessToken;

    public ThreadsApiClient(HttpClient http, Config config, ILogger<ThreadsApiClient> log)
    {
        _http = http;
        _config = config;
        _log = log;
        _accessToken = config.ThreadsAccessToken;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(GetAccessToken());

    public async Task EnsureAccessTokenFreshAsync(CancellationToken ct = default)
    {
        var token = GetAccessToken();
        if (string.IsNullOrWhiteSpace(token))
            return;

        if (!ShouldRefreshProactively(DateTimeOffset.UtcNow))
            return;

        var refreshed = await TryRefreshAccessTokenAsync(ct);
        if (!refreshed)
            _log.LogWarning("Threads proactive token refresh did not succeed. Existing token will remain configured.");
    }

    public async Task SendAsync(string text)
    {
        _ = await SendPostAsync(text);
    }

    public async Task<bool> TrySendAsync(string text)
    {
        return !string.IsNullOrWhiteSpace(await SendPostAsync(text));
    }

    public async Task<string?> SendPostAsync(string text)
    {
        text ??= "";
        if (string.IsNullOrWhiteSpace(text))
        {
            _log.LogWarning("Threads send skipped because text was empty.");
            return null;
        }

        if (text.Length > MaxTextLength)
        {
            _log.LogWarning("Threads send skipped because text length {Length} exceeds limit {Limit}.", text.Length, MaxTextLength);
            return null;
        }

        var token = GetAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            _log.LogInformation("Threads credentials not configured. Would have posted: {Content}", text);
            return null;
        }

        const int maxAttempts = 2;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var creation = await CreateTextContainerAsync(text, token);
            if (creation.Success && !string.IsNullOrWhiteSpace(creation.Id))
            {
                var publish = await PublishCreatedContainerAsync(creation.Id, token);
                if (publish.Success && !string.IsNullOrWhiteSpace(publish.Id))
                    return publish.Id;

                if (publish.ShouldRefreshToken && attempt < maxAttempts)
                {
                    var refreshed = await TryRefreshAccessTokenAsync();
                    if (refreshed)
                    {
                        token = GetAccessToken();
                        if (!string.IsNullOrWhiteSpace(token))
                            continue;
                    }
                }

                return null;
            }

            if (creation.ShouldRefreshToken && attempt < maxAttempts)
            {
                var refreshed = await TryRefreshAccessTokenAsync();
                if (refreshed)
                {
                    token = GetAccessToken();
                    if (!string.IsNullOrWhiteSpace(token))
                        continue;
                }
            }

            return null;
        }

        return null;
    }

    public async Task<string?> SendImagePostAsync(string text, string imageUrl)
    {
        text ??= "";
        imageUrl = imageUrl?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            _log.LogWarning("Threads image send skipped because image URL was empty.");
            return null;
        }

        if (text.Length > MaxTextLength)
        {
            _log.LogWarning("Threads image send skipped because text length {Length} exceeds limit {Limit}.", text.Length, MaxTextLength);
            return null;
        }

        var token = GetAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            _log.LogInformation("Threads credentials not configured. Would have posted image {ImageUrl} with content: {Content}", imageUrl, text);
            return null;
        }

        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var creation = await CreateImageContainerAsync(text, imageUrl, token);
            if (creation.Success && !string.IsNullOrWhiteSpace(creation.Id))
            {
                _log.LogInformation(
                    "Threads image container created successfully with creationId {CreationId}, textLength {TextLength}, imageUrl {ImageUrl}",
                    creation.Id,
                    text.Length,
                    imageUrl);

                var publish = await PublishCreatedContainerAsync(creation.Id, token);
                if (publish.Success && !string.IsNullOrWhiteSpace(publish.Id))
                    return publish.Id;

                if (publish.ShouldRefreshToken && attempt < maxAttempts)
                {
                    var refreshed = await TryRefreshAccessTokenAsync();
                    if (refreshed)
                    {
                        token = GetAccessToken();
                        if (!string.IsNullOrWhiteSpace(token))
                            continue;
                    }
                }

                return null;
            }

            if (creation.ShouldRefreshToken && attempt < maxAttempts)
            {
                var refreshed = await TryRefreshAccessTokenAsync();
                if (refreshed)
                {
                    token = GetAccessToken();
                    if (!string.IsNullOrWhiteSpace(token))
                        continue;
                }
            }

            return null;
        }

        return null;
    }

    private async Task<(bool Success, string? Id, bool ShouldRefreshToken)> CreateTextContainerAsync(string text, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, CreateThreadEndpoint);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("media_type", "TEXT"),
            new KeyValuePair<string, string>("text", text)
        });

        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            _log.LogError("Threads create container failed: {StatusCode} {Body}", res.StatusCode, json);
            return (false, null, ShouldRefreshToken(res.StatusCode, json));
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("id", out var idEl))
                return (true, idEl.GetString(), false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Threads create container response parse failed: {Json}", json);
            return (false, null, false);
        }

        _log.LogWarning("Threads create container returned no id.");
        return (false, null, false);
    }

    private async Task<(bool Success, string? Id, bool ShouldRefreshToken)> CreateImageContainerAsync(string text, string imageUrl, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, CreateThreadEndpoint);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("media_type", "IMAGE"),
            new KeyValuePair<string, string>("image_url", imageUrl),
            new KeyValuePair<string, string>("text", text)
        });

        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            _log.LogError("Threads create image container failed: {StatusCode} {Body}", res.StatusCode, json);
            return (false, null, ShouldRefreshToken(res.StatusCode, json));
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("id", out var idEl))
                return (true, idEl.GetString(), false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Threads create image container response parse failed: {Json}", json);
            return (false, null, false);
        }

        _log.LogWarning("Threads create image container returned no id.");
        return (false, null, false);
    }

    private async Task<(bool Success, string? Id, bool ShouldRefreshToken)> PublishCreatedContainerAsync(string creationId, string token)
    {
        var ready = await WaitForContainerReadyAsync(creationId, token);
        if (!ready.IsReady)
        {
            if (ready.ShouldRefreshToken)
                return (false, null, true);

            return (false, null, false);
        }

        for (var attempt = 0; attempt <= PublishRetryDelaysMs.Length; attempt++)
        {
            var publish = await PublishContainerAsync(creationId, token);
            if (publish.Success && !string.IsNullOrWhiteSpace(publish.Id))
                return (true, publish.Id, false);

            if (publish.ShouldRefreshToken)
                return (false, null, true);

            if (publish.IsTransientFailure && attempt < PublishRetryDelaysMs.Length)
            {
                var delayMs = PublishRetryDelaysMs[attempt];
                _log.LogWarning(
                    "Threads publish transient failure for creationId {CreationId}: {StatusCode} {Body}. Retrying in {DelayMs}ms.",
                    creationId,
                    publish.StatusCode,
                    publish.ErrorBody,
                    delayMs);
                await Task.Delay(delayMs);
                continue;
            }

            if (publish.StatusCode.HasValue)
                _log.LogError("Threads publish failed for creationId {CreationId}: {StatusCode} {Body}", creationId, publish.StatusCode, publish.ErrorBody);

            return (false, null, false);
        }

        return (false, null, false);
    }

    private async Task<(bool IsReady, bool ShouldRefreshToken)> WaitForContainerReadyAsync(string creationId, string token)
    {
        for (var attempt = 0; attempt <= ContainerReadyPollDelaysMs.Length; attempt++)
        {
            var status = await GetContainerStatusAsync(creationId, token);
            if (status.ShouldRefreshToken)
                return (false, true);

            if (status.IsFinished)
                return (true, false);

            if (status.IsError)
            {
                _log.LogError(
                    "Threads container {CreationId} entered error state before publish. Status={Status} ErrorMessage={ErrorMessage}",
                    creationId,
                    status.Status,
                    status.ErrorMessage);
                return (false, false);
            }

            if (attempt >= ContainerReadyPollDelaysMs.Length)
            {
                _log.LogWarning(
                    "Threads container {CreationId} did not reach FINISHED before timeout. LastStatus={Status} ErrorMessage={ErrorMessage}",
                    creationId,
                    status.Status,
                    status.ErrorMessage);
                return (false, false);
            }

            var delayMs = ContainerReadyPollDelaysMs[attempt];
            _log.LogInformation(
                "Threads container {CreationId} not ready yet. Status={Status}. Polling again in {DelayMs}ms.",
                creationId,
                status.Status,
                delayMs);
            await Task.Delay(delayMs);
        }

        return (false, false);
    }

    private async Task<(bool Success, string? Id, bool ShouldRefreshToken, bool IsTransientFailure, HttpStatusCode? StatusCode, string? ErrorBody)> PublishContainerAsync(string creationId, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, PublishThreadEndpoint);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("creation_id", creationId)
        });

        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            return (
                false,
                null,
                ShouldRefreshToken(res.StatusCode, json),
                IsTransientThreadsError(res.StatusCode, json),
                res.StatusCode,
                json);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("id", out var idEl))
                return (true, idEl.GetString(), false, false, null, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Threads publish response parse failed: {Json}", json);
            return (false, null, false, false, null, json);
        }

        _log.LogWarning("Threads publish returned no id.");
        return (false, null, false, false, null, json);
    }

    private async Task<(bool IsFinished, bool IsError, bool ShouldRefreshToken, string? Status, string? ErrorMessage)> GetContainerStatusAsync(string creationId, string token)
    {
        var url = $"https://graph.threads.net/{Uri.EscapeDataString(creationId)}?fields={Uri.EscapeDataString(ContainerFields)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            _log.LogError("Threads container status check failed for creationId {CreationId}: {StatusCode} {Body}", creationId, res.StatusCode, json);
            return (false, false, ShouldRefreshToken(res.StatusCode, json), null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
            var errorMessage = root.TryGetProperty("error_message", out var errorEl) ? errorEl.GetString() : null;

            var normalizedStatus = (status ?? "").Trim();

            var isFinished = string.Equals(normalizedStatus, "FINISHED", StringComparison.OrdinalIgnoreCase);

            var isError =
                string.Equals(normalizedStatus, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedStatus, "EXPIRED", StringComparison.OrdinalIgnoreCase);

            return (isFinished, isError, false, status, errorMessage);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Threads container status response parse failed for creationId {CreationId}: {Json}", creationId, json);
            return (false, false, false, null, null);
        }
    }

    private string? GetAccessToken()
    {
        lock (_tokenLock)
        {
            if (!string.IsNullOrWhiteSpace(_accessToken)) return _accessToken;
            _accessToken = _config.ThreadsAccessToken;
            return _accessToken;
        }
    }

    private async Task<bool> TryRefreshAccessTokenAsync(CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            var currentToken = GetAccessToken();
            if (string.IsNullOrWhiteSpace(currentToken))
            {
                _log.LogWarning("Threads access token not configured. Cannot refresh.");
                return false;
            }

            var attemptUtc = DateTimeOffset.UtcNow;
            _config.ThreadsAccessTokenLastRefreshAttemptAtUtc = FormatUtc(attemptUtc);

            var url = $"{RefreshAccessTokenEndpoint}?grant_type=th_refresh_token&access_token={Uri.EscapeDataString(currentToken)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);

            var res = await _http.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                _log.LogError("Threads token refresh failed: {StatusCode} {Body}", res.StatusCode, json);
                await SaveConfigAsync();
                return false;
            }

            string? newAccessToken = null;
            long? expiresInSeconds = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("access_token", out var tokenEl))
                    newAccessToken = tokenEl.GetString();
                if (doc.RootElement.TryGetProperty("expires_in", out var expiresEl) && expiresEl.TryGetInt64(out var expiresInValue))
                    expiresInSeconds = expiresInValue;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Threads token refresh response parse failed: {Json}", json);
                await SaveConfigAsync();
                return false;
            }

            if (string.IsNullOrWhiteSpace(newAccessToken))
            {
                _log.LogError("Threads token refresh did not return access_token.");
                await SaveConfigAsync();
                return false;
            }

            lock (_tokenLock)
            {
                _accessToken = newAccessToken;
                _config.ThreadsAccessToken = newAccessToken;
                if (expiresInSeconds is > 0)
                    _config.ThreadsAccessTokenExpiresAtUtc = FormatUtc(attemptUtc.AddSeconds(expiresInSeconds.Value));
            }

            await SaveConfigAsync();

            _log.LogInformation("Threads access token refreshed successfully.");
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static bool ShouldRefreshToken(HttpStatusCode statusCode, string? responseBody)
    {
        if (statusCode == HttpStatusCode.Unauthorized)
            return true;

        if (string.IsNullOrWhiteSpace(responseBody))
            return false;

        return responseBody.Contains("Invalid OAuth access token", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("Error validating access token", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("Session has expired", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsTransientThreadsError(HttpStatusCode statusCode, string? responseBody)
    {
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    if (error.TryGetProperty("is_transient", out var isTransient) &&
                        isTransient.ValueKind == JsonValueKind.True)
                        return true;

                    if (error.TryGetProperty("code", out var code) &&
                        code.TryGetInt32(out var codeValue) &&
                        codeValue == 2)
                        return true;
                }
            }
            catch (JsonException)
            {
            }
        }

        var numericStatusCode = (int)statusCode;
        return statusCode == HttpStatusCode.RequestTimeout ||
               numericStatusCode == 429 ||
               numericStatusCode >= 500;
    }

    private bool ShouldRefreshProactively(DateTimeOffset nowUtc)
    {
        return ShouldRefreshProactively(
            nowUtc,
            _config.ThreadsAccessTokenExpiresAtUtc,
            _config.ThreadsAccessTokenLastRefreshAttemptAtUtc);
    }

    internal static bool ShouldRefreshProactively(DateTimeOffset nowUtc, string? expiresAtUtc, string? lastRefreshAttemptAtUtc)
    {
        var expiresAt = ParseConfigUtc(expiresAtUtc);
        if (expiresAt.HasValue)
            return expiresAt.Value - nowUtc <= ProactiveRefreshWindow;

        var lastAttempt = ParseConfigUtc(lastRefreshAttemptAtUtc);
        return !lastAttempt.HasValue || nowUtc - lastAttempt.Value >= UnknownExpiryRefreshRetryInterval;
    }

    private async Task SaveConfigAsync()
    {
        try
        {
            await _config.SaveAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to save Threads access token config.");
        }
    }

    private static DateTimeOffset? ParseConfigUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!DateTimeOffset.TryParse(value, out var parsed))
            return null;

        return parsed.ToUniversalTime();
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("O");
    }

}
