using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using NBitcoin.Secp256k1;
using SHA256 = System.Security.Cryptography.SHA256;

namespace LongevityWorldCup.Website.Tools;

public sealed record NostrEvent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("pubkey")] string PublicKey,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("kind")] int Kind,
    [property: JsonPropertyName("tags")] string[][] Tags,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("sig")] string Signature);

public static class NostrProtocol
{
    // An application budget, not a protocol limit. Keep notes within our relay frame budget.
    public const int MaxCharacters = 16_000;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static string GetPublicKey(string privateKeyHex)
    {
        using var key = ReadPrivateKey(privateKeyHex);
        Span<byte> publicKey = stackalloc byte[32];
        key.CreateXOnlyPubKey().WriteToSpan(publicKey);
        return Convert.ToHexStringLower(publicKey);
    }

    public static NostrEvent Sign(string privateKeyHex, string expectedPublicKey, string content,
        long createdAt, int kind = 1, string[][]? tags = null)
    {
        using var key = ReadPrivateKey(privateKeyHex);
        Span<byte> publicKeyBytes = stackalloc byte[32];
        key.CreateXOnlyPubKey().WriteToSpan(publicKeyBytes);
        var publicKey = Convert.ToHexStringLower(publicKeyBytes);
        if (!string.Equals(publicKey, expectedPublicKey, StringComparison.Ordinal))
            throw new InvalidOperationException("NostrAccountMismatch");
        var unsigned = new NostrEvent("", publicKey, createdAt, kind, tags ?? [], content, "");
        var hash = SHA256.HashData(Utf8.GetBytes(CanonicalJson(unsigned)));
        Span<byte> signature = stackalloc byte[64];
        key.SignBIP340(hash).WriteToSpan(signature);
        return unsigned with { Id = Convert.ToHexStringLower(hash), Signature = Convert.ToHexStringLower(signature) };
    }

    public static bool Verify(NostrEvent? item, string expectedPublicKey)
    {
        try
        {
            if (item is null || item.PublicKey != expectedPublicKey || item.Kind is < 0 or > 65535 || item.CreatedAt < 0
                || !IsLowerHex(item.Id, 32) || !IsLowerHex(item.PublicKey, 32) || !IsLowerHex(item.Signature, 64)) return false;
            var hash = SHA256.HashData(Utf8.GetBytes(CanonicalJson(item)));
            return Convert.ToHexStringLower(hash) == item.Id
                && ECXOnlyPubKey.TryCreate(Convert.FromHexString(item.PublicKey), out var key)
                && SecpSchnorrSignature.TryCreate(Convert.FromHexString(item.Signature), out var signature)
                && key.SigVerifyBIP340(signature, hash);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    // NIP-01 hashes literal Unicode JSON. General JSON encoders may escape HTML or U+2028,
    // producing a different event ID even when the decoded content is identical.
    public static string CanonicalJson(NostrEvent item)
    {
        if (item.Kind is < 0 or > 65535 || item.CreatedAt < 0 || item.Tags is null)
            throw new ArgumentException("InvalidNostrEvent");
        var json = new StringBuilder("[0,");
        AppendString(json, item.PublicKey);
        json.Append(',').Append(item.CreatedAt.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(',').Append(item.Kind.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",[");
        for (var i = 0; i < item.Tags.Length; i++)
        {
            if (i > 0) json.Append(',');
            json.Append('[');
            var tag = item.Tags[i] ?? throw new ArgumentException("InvalidNostrTags");
            for (var j = 0; j < tag.Length; j++)
            {
                if (j > 0) json.Append(',');
                AppendString(json, tag[j]);
            }
            json.Append(']');
        }
        json.Append("],");
        AppendString(json, item.Content);
        return json.Append(']').ToString();
    }

    private static void AppendString(StringBuilder json, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        json.Append('"');
        foreach (var c in value)
        {
            json.Append(c switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r",
                '\t' => "\\t", '\b' => "\\b", '\f' => "\\f",
                < ' ' => "\\u" + ((int)c).ToString("x4"),
                _ => c.ToString()
            });
        }
        json.Append('"');
    }

    private static ECPrivKey ReadPrivateKey(string hex)
    {
        if (!IsLowerHex(hex, 32)) throw new ArgumentException("InvalidNostrPrivateKey");
        var bytes = Convert.FromHexString(hex);
        try
        {
            if (!ECPrivKey.TryCreate(bytes, out var key)) throw new ArgumentException("InvalidNostrPrivateKey");
            return key;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static bool IsLowerHex(string? value, int byteCount) => value?.Length == byteCount * 2
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static string EncodeIdentifier(string hex, string prefix)
    {
        if (prefix is not ("npub" or "nsec" or "note") || !IsLowerHex(hex, 32))
            throw new ArgumentException("InvalidNostrIdentifier");
        const string alphabet = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
        var data = new List<int>();
        var accumulator = 0;
        var bits = 0;
        foreach (var b in Convert.FromHexString(hex))
        {
            accumulator = ((accumulator << 8) | b) & 0xffff;
            bits += 8;
            while (bits >= 5) { bits -= 5; data.Add((accumulator >> bits) & 31); }
        }
        if (bits > 0) data.Add((accumulator << (5 - bits)) & 31);
        var expanded = prefix.Select(c => c >> 5).Concat([0]).Concat(prefix.Select(c => c & 31));
        uint checksum = 1;
        uint[] generators = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];
        foreach (var value in expanded.Concat(data).Concat(Enumerable.Repeat(0, 6)))
        {
            var top = checksum >> 25;
            checksum = ((checksum & 0x1ffffff) << 5) ^ (uint)value;
            for (var i = 0; i < 5; i++) if (((top >> i) & 1) != 0) checksum ^= generators[i];
        }
        checksum ^= 1; // NIP-19 uses Bech32, not Bech32m.
        return prefix + "1" + string.Concat(data.Select(i => alphabet[i]))
            + string.Concat(Enumerable.Range(0, 6).Select(i => alphabet[(int)((checksum >> (5 * (5 - i))) & 31)]));
    }
}
