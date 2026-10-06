using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Tools;

internal static partial class BlueskyPost
{
    internal const int MaxGraphemes = 300;

    internal static bool Fits(string text) =>
        new StringInfo(text).LengthInTextElements <= MaxGraphemes && Encoding.UTF8.GetByteCount(text) <= 3000;

    internal static string Truncate(string text, int maxGraphemes)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        return starts.Length <= maxGraphemes ? text : text[..starts[Math.Max(0, maxGraphemes - 1)]].TrimEnd() + "…";
    }

    internal static JsonObject CreateRecord(string text, DateTimeOffset createdAt)
    {
        if (!Fits(text)) throw new ArgumentException("Bluesky posts must fit 300 graphemes and 3000 UTF-8 bytes.", nameof(text));
        var facets = new JsonArray();
        foreach (Match match in Links().Matches(text))
        {
            var link = match.Value.TrimEnd('.', ',', '!', '?', ';', ':', ')', ']', '}');
            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) continue;
            var start = Encoding.UTF8.GetByteCount(text.AsSpan(0, match.Index));
            facets.Add(new JsonObject
            {
                ["index"] = new JsonObject { ["byteStart"] = start, ["byteEnd"] = start + Encoding.UTF8.GetByteCount(link) },
                ["features"] = new JsonArray(new JsonObject { ["$type"] = "app.bsky.richtext.facet#link", ["uri"] = link })
            });
        }
        return new JsonObject
        {
            ["$type"] = "app.bsky.feed.post",
            ["text"] = text,
            ["createdAt"] = createdAt.ToUniversalTime().ToString("o"),
            ["langs"] = new JsonArray("en"),
            ["facets"] = facets
        };
    }

    // A valid timestamp ID is saved before the first network write and reused on every retry.
    internal static string NewRecordKey(DateTimeOffset now)
    {
        const string alphabet = "234567abcdefghijklmnopqrstuvwxyz";
        var microseconds = (ulong)((now.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10);
        var value = (microseconds << 10) | (uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1024);
        Span<char> result = stackalloc char[13];
        for (var i = 12; i >= 0; i--) { result[i] = alphabet[(int)(value & 31)]; value >>= 5; }
        return new string(result);
    }

    [GeneratedRegex(@"https?://[^\s<>|]+", RegexOptions.IgnoreCase)]
    private static partial Regex Links();
}
