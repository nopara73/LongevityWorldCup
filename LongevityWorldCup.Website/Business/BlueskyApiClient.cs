using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace LongevityWorldCup.Website.Business;

public sealed record BlueskyPostReceipt(string Uri, string Cid);

internal sealed class BlueskyApiException(string code, HttpStatusCode status) : Exception($"Bluesky API failed: {code} ({(int)status}).")
{
    internal string Code { get; } = code;
    internal HttpStatusCode Status { get; } = status;
}

public sealed class BlueskyApiClient
{
    private const string Collection = "app.bsky.feed.post";
    private readonly HttpClient _http;
    private readonly Config _config;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessJwt;
    private string? _refreshJwt;
    private string? _did;
    private Uri? _pds;

    public BlueskyApiClient(IHttpClientFactory factory, Config config)
    {
        _http = factory.CreateClient(nameof(BlueskyApiClient));
        _config = config;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_config.BlueskyIdentifier) &&
                                !string.IsNullOrWhiteSpace(_config.BlueskyAppPassword);

    internal async Task<JsonObject> UploadImageAsync(byte[] bytes, string contentType, CancellationToken ct)
    {
        if (bytes.Length > 2_000_000) throw new ArgumentException("Bluesky images must fit 2 MB.", nameof(bytes));
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureSessionAsync(ct);
            var response = await AuthenticatedAsync(HttpMethod.Post, "com.atproto.repo.uploadBlob", () =>
            {
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                return content;
            }, ct);
            var blob = response["blob"]?.DeepClone() as JsonObject
                ?? throw new BlueskyApiException("MissingBlob", HttpStatusCode.BadGateway);
            if (blob["$type"]?.GetValue<string>() != "blob" || string.IsNullOrWhiteSpace(blob["ref"]?["$link"]?.GetValue<string>())
                || blob["mimeType"]?.GetValue<string>() != contentType || blob["size"]?.GetValue<int>() is not (> 0 and <= 2_000_000))
                throw new BlueskyApiException("InvalidBlob", HttpStatusCode.BadGateway);
            return blob;
        }
        finally { _gate.Release(); }
    }

    public async Task<BlueskyPostReceipt> SendRecordAsync(string recordKey, JsonObject record, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureSessionAsync(ct);
            var existing = await FindRecordAsync(recordKey, record, ct);
            if (existing is not null) return existing;
            try
            {
                var result = await AuthenticatedAsync(HttpMethod.Post, "com.atproto.repo.createRecord", () => JsonContent.Create(new
                {
                    repo = _did, collection = Collection, rkey = recordKey, validate = true, record
                }), ct);
                return ReadReceipt(result, recordKey);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not ArgumentException)
            {
                // A timeout or a lost response may follow a successful write. Never mint a new key.
                existing = await FindRecordAsync(recordKey, record, ct);
                if (existing is not null) return existing;
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<BlueskyPostReceipt?> FindRecordAsync(string key, JsonObject expectedRecord, CancellationToken ct)
    {
        try
        {
            var result = await AuthenticatedAsync(HttpMethod.Get,
                $"com.atproto.repo.getRecord?repo={Uri.EscapeDataString(_did!)}&collection={Collection}&rkey={Uri.EscapeDataString(key)}", null, ct);
            if (!JsonNode.DeepEquals(result["value"], expectedRecord))
                throw new BlueskyApiException("RecordKeyConflict", HttpStatusCode.Conflict);
            return ReadReceipt(result, key);
        }
        catch (BlueskyApiException ex) when (ex.Code == "RecordNotFound") { return null; }
    }

    private BlueskyPostReceipt ReadReceipt(JsonObject result, string key)
    {
        var uri = result["uri"]?.GetValue<string>();
        var cid = result["cid"]?.GetValue<string>();
        if (uri != $"at://{_did}/{Collection}/{key}" || string.IsNullOrWhiteSpace(cid))
            throw new BlueskyApiException("MissingOrInvalidReceipt", HttpStatusCode.BadGateway);
        return new(uri, cid);
    }

    private async Task EnsureSessionAsync(CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Bluesky is not configured.");
        if (_accessJwt is not null) return;
        _pds = ValidateServiceUri(_config.BlueskyServiceUrl);
        var session = await RequestAsync(_pds, HttpMethod.Post, "com.atproto.server.createSession", null,
            JsonContent.Create(new { identifier = _config.BlueskyIdentifier, password = _config.BlueskyAppPassword }), ct);
        ApplySession(session);
    }

    private async Task<JsonObject> AuthenticatedAsync(HttpMethod method, string endpoint, Func<HttpContent>? content, CancellationToken ct)
    {
        try { return await RequestAsync(_pds!, method, endpoint, _accessJwt, content?.Invoke(), ct); }
        catch (BlueskyApiException ex) when (ex.Code is "ExpiredToken" or "InvalidToken" || ex.Status == HttpStatusCode.Unauthorized)
        {
            try
            {
                var session = await RequestAsync(_pds!, HttpMethod.Post, "com.atproto.server.refreshSession", _refreshJwt, null, ct);
                ApplySession(session);
            }
            catch (BlueskyApiException refreshError) when (refreshError.Status == HttpStatusCode.Unauthorized || refreshError.Code is "ExpiredToken" or "InvalidToken")
            {
                _accessJwt = null;
                await EnsureSessionAsync(ct);
            }
            return await RequestAsync(_pds!, method, endpoint, _accessJwt, content?.Invoke(), ct);
        }
    }

    private void ApplySession(JsonObject session)
    {
        var accessJwt = session["accessJwt"]?.GetValue<string>() ?? throw new BlueskyApiException("MissingAccessToken", HttpStatusCode.BadGateway);
        var refreshJwt = session["refreshJwt"]?.GetValue<string>() ?? throw new BlueskyApiException("MissingRefreshToken", HttpStatusCode.BadGateway);
        var did = session["did"]?.GetValue<string>() ?? throw new BlueskyApiException("MissingDid", HttpStatusCode.BadGateway);
        if (_config.BlueskyIdentifier?.StartsWith("did:", StringComparison.Ordinal) == true && did != _config.BlueskyIdentifier)
            throw new BlueskyApiException("AccountChanged", HttpStatusCode.Conflict);
        if (_did is not null && did != _did) throw new BlueskyApiException("AccountChanged", HttpStatusCode.Conflict);
        var pds = _pds;
        if (session["didDoc"]?["service"] is JsonArray services)
        {
            var endpoint = services.OfType<JsonObject>().FirstOrDefault(x =>
                x["type"]?.GetValue<string>() == "AtprotoPersonalDataServer")?["serviceEndpoint"]?.GetValue<string>();
            if (endpoint is not null) pds = ValidateServiceUri(endpoint);
        }
        _accessJwt = accessJwt;
        _refreshJwt = refreshJwt;
        _did = did;
        _pds = pds;
    }

    private async Task<JsonObject> RequestAsync(Uri service, HttpMethod method, string endpoint, string? bearer, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(service, "/xrpc/" + endpoint)) { Content = content };
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await _http.SendAsync(request, ct);
        JsonObject? body = null;
        try { body = await response.Content.ReadFromJsonAsync<JsonObject>(ct); }
        catch (System.Text.Json.JsonException) { }
        if (!response.IsSuccessStatusCode)
        {
            // Service messages can contain credentials or submitted text. Log only the bounded error identifier.
            var code = body?["error"]?.GetValue<string>() ?? "HttpError";
            if (code.Length > 80 || code.Any(c => !char.IsAsciiLetterOrDigit(c))) code = "HttpError";
            throw new BlueskyApiException(code, response.StatusCode);
        }
        return body ?? throw new BlueskyApiException("InvalidJsonResponse", HttpStatusCode.BadGateway);
    }

    private static Uri ValidateServiceUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Bluesky service must be an HTTPS URL without credentials, query, or fragment.");
        return uri;
    }
}
