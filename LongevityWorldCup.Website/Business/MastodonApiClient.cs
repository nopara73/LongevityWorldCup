using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

internal sealed record MastodonPostRequest(string Text, string? MediaId = null);
internal sealed record MastodonPostReceipt(string Id, string Url);

public sealed class MastodonApiException(string code, bool outcomeUnknown = false, TimeSpan? retryAfter = null) : Exception(code)
{
    public string Code { get; } = code;
    public bool OutcomeUnknown { get; } = outcomeUnknown;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class MastodonApiClient(Config config, IHttpClientFactory clients, TimeProvider time)
{
    private readonly SemaphoreSlim _identityGate = new(1, 1);
    private string? _verifiedIdentity;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config.MastodonAccountId)
        && !string.IsNullOrWhiteSpace(config.MastodonAccessToken) && TryServerUri(out _);

    private bool TryServerUri(out Uri? uri) => Uri.TryCreate(config.MastodonServerUrl, UriKind.Absolute, out uri)
        && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/";

    internal async Task VerifyAccountAsync(CancellationToken ct)
    {
        if (!IsConfigured) throw new MastodonApiException("NotConfigured");
        var identity = string.Join('\n', config.MastodonServerUrl, config.MastodonAccountId, config.MastodonAccessToken);
        if (_verifiedIdentity == identity) return;
        await _identityGate.WaitAsync(ct);
        try
        {
            if (_verifiedIdentity == identity) return;
            var account = await SendAsync(HttpMethod.Get, "api/v1/accounts/verify_credentials", null, false, ct);
            if (account["id"]?.GetValue<string>() != config.MastodonAccountId
                || account["suspended"]?.GetValue<bool>() == true)
                throw new MastodonApiException("AccountMismatchOrSuspended");
            _verifiedIdentity = identity;
        }
        finally { _identityGate.Release(); }
    }

    internal async Task<string> UploadImageAsync(byte[] bytes, string contentType, string description, CancellationToken ct)
    {
        await VerifyAccountAsync(ct);
        if (bytes.Length > 16 * 1024 * 1024 || contentType is not ("image/png" or "image/jpeg" or "image/webp"))
            throw new MastodonApiException("InvalidImage");
        using var multipart = new MultipartFormDataContent();
        using var image = new ByteArrayContent(bytes);
        image.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(image, "file", contentType == "image/png" ? "announcement.png" : "announcement.jpg");
        var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(description);
        multipart.Add(new StringContent(boundaries.Length > 10_000 ? description[..boundaries[10_000]] : description), "description");
        var media = await SendAsync(HttpMethod.Post, "api/v2/media", multipart, false, ct);
        var id = media["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id)) throw new MastodonApiException("MissingMediaId");
        for (var attempt = 0; attempt < 10 && media["url"] is null; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), time, ct);
            media = await SendAsync(HttpMethod.Get, "api/v1/media/" + Uri.EscapeDataString(id), null, false, ct);
        }
        if (media["url"] is null) throw new MastodonApiException("MediaStillProcessing");
        return id;
    }

    internal async Task<MastodonPostReceipt> PublishAsync(string key, MastodonPostRequest post, CancellationToken ct)
    {
        await VerifyAccountAsync(ct);
        if (string.IsNullOrWhiteSpace(post.Text) || MastodonPost.Count(post.Text) > MastodonPost.MaxCharacters)
            throw new MastodonApiException("InvalidPostText");
        var fields = new List<KeyValuePair<string, string>>
        {
            new("status", post.Text), new("visibility", "public"), new("language", "en")
        };
        if (post.MediaId is not null) fields.Add(new("media_ids[]", post.MediaId));
        using var content = new FormUrlEncodedContent(fields);
        var result = await SendAsync(HttpMethod.Post, "api/v1/statuses", content, true, ct, key);
        try
        {
            var id = result["id"]?.GetValue<string>();
            var url = result["url"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || !TryServerUri(out var server) || uri.Scheme != "https" || uri.Authority != server!.Authority
                || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
                || result["account"]?["id"]?.GetValue<string>() != config.MastodonAccountId)
                throw new MastodonApiException("InvalidPostReceipt", outcomeUnknown: true);
            return new(id, url!);
        }
        catch (MastodonApiException) { throw; }
        catch (Exception) { throw new MastodonApiException("InvalidPostReceipt", outcomeUnknown: true); }
    }

    private async Task<JsonObject> SendAsync(HttpMethod method, string path, HttpContent? content, bool publishing,
        CancellationToken ct, string? idempotencyKey = null)
    {
        if (!TryServerUri(out var server)) throw new MastodonApiException("InvalidServerUrl");
        using var client = clients.CreateClient(nameof(MastodonApiClient));
        using var request = new HttpRequestMessage(method, new Uri(server!, path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.MastodonAccessToken);
        request.Headers.UserAgent.ParseAdd("LongevityWorldCup/1.0");
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var retry = response.Headers.RetryAfter?.Delta;
                if (response.Headers.RetryAfter?.Date is { } retryDate)
                    retry = retryDate - time.GetUtcNow();
                throw new MastodonApiException("Http" + (int)response.StatusCode,
                    publishing && ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout), retry);
            }
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonNode.Parse(json)?.AsObject() ?? throw new MastodonApiException("InvalidResponse", publishing);
        }
        catch (MastodonApiException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new MastodonApiException("Timeout", publishing); }
        catch (HttpRequestException) { throw new MastodonApiException("NetworkError", publishing); }
        catch (System.Text.Json.JsonException) { throw new MastodonApiException("InvalidResponse", publishing); }
        catch (InvalidOperationException) { throw new MastodonApiException("InvalidResponse", publishing); }
    }
}
