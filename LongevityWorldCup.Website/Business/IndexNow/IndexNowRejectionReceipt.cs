using System.Text;
using System.Text.Json;

namespace LongevityWorldCup.Website.Business.IndexNow;

public sealed record IndexNowRejectionReceipt(
    DateTimeOffset SubmittedAtUtc,
    int StatusCode,
    int UrlCount,
    string? ReasonPhrase,
    string? ContentType,
    DateTimeOffset? ResponseDate,
    IReadOnlyDictionary<string, string> RequestIds,
    string? BodyPreview,
    bool BodyTruncated,
    string? BodyReadError)
{
    private const int BodyLimit = 4096;
    private const int HeaderLimit = 256;
    private static readonly string[] RequestIdHeaders =
    [
        "Request-Id", "X-Request-Id", "X-MS-Request-Id", "apim-request-id",
        "Correlation-Id", "X-Correlation-Id", "Traceparent", "X-Trace-Id",
        "X-Azure-Ref", "X-MSEdge-Ref", "X-BingApis-TraceId", "CF-Ray"
    ];

    internal static IndexNowRejectionReceipt Capture(HttpResponseMessage response, DateTimeOffset submittedAtUtc, int urlCount, string key)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in RequestIdHeaders)
        {
            if (response.Headers.TryGetValues(name, out var values) || response.TrailingHeaders.TryGetValues(name, out values))
                ids[name] = Limit(Redact(string.Join(", ", values.Take(3)), key), HeaderLimit);
        }

        string? body = null;
        string? readError = null;
        var truncated = false;
        try
        {
            // PostAsJsonAsync has already buffered the response. Read a bounded prefix, including
            // enough lookahead to redact a key that straddles the end of the preview.
            using var reader = new StreamReader(response.Content.ReadAsStream(), Encoding.UTF8);
            var buffer = new char[BodyLimit + key.Length + 1];
            var length = reader.ReadBlock(buffer, 0, buffer.Length);
            if (length > 0)
            {
                var text = new string(buffer, 0, length);
                var end = Math.Min(length, BodyLimit);
                if (length > BodyLimit)
                {
                    var crossingKey = text.IndexOf(key, BodyLimit - key.Length + 1, StringComparison.OrdinalIgnoreCase);
                    if (crossingKey >= 0 && crossingKey < BodyLimit) end = crossingKey;
                }
                var redacted = Redact(Limit(text, end), key);
                truncated = length > BodyLimit || redacted.Length > BodyLimit;
                body = Limit(redacted, BodyLimit);
                if (length <= BodyLimit && text.AsSpan().TrimStart().StartsWith("{", StringComparison.Ordinal))
                    ReadBodyRequestIds(text, key, ids);
            }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or NotSupportedException or ObjectDisposedException)
        {
            readError = ex.GetType().Name;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        return new(submittedAtUtc, (int)response.StatusCode, urlCount,
            response.ReasonPhrase is { } reason ? Limit(Redact(reason, key), HeaderLimit) : null,
            mediaType is null ? null : Limit(Redact(mediaType, key), HeaderLimit),
            response.Headers.Date, ids, body, truncated, readError);
    }

    private static void ReadBodyRequestIds(string body, string key, Dictionary<string, string> ids)
    {
        try
        {
            using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = json.RootElement;
            AddIds(root, "body");
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                AddIds(error, "body.error");
        }
        catch (JsonException)
        {
            // The preview also preserves non-JSON errors and incomplete JSON.
        }

        void AddIds(JsonElement obj, string prefix)
        {
            foreach (var property in obj.EnumerateObject())
            {
                var name = property.Name.Replace("_", "").Replace("-", "").ToLowerInvariant();
                if ((name is "requestid" or "correlationid" or "traceid") && property.Value.ValueKind == JsonValueKind.String)
                    ids[$"{prefix}.{name}"] = Limit(Redact(property.Value.GetString()!, key), HeaderLimit);
            }
        }
    }

    private static string Redact(string value, string key) => value.Replace(key, "[redacted]", StringComparison.OrdinalIgnoreCase);

    private static string Limit(string value, int limit) => value.Length <= limit
        ? value
        : value[..(limit > 0 && char.IsHighSurrogate(value[limit - 1]) ? limit - 1 : limit)];
}
