using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace LongevityWorldCup.Website.Business;

public sealed record WebPushKeys(string P256dh, string Auth);
public sealed record WebPushSubscription(string Endpoint, WebPushKeys Keys);
internal sealed record WebPushResult(HttpStatusCode Status, TimeSpan? RetryAfter = null);

/// <summary>RFC 8291 aes128gcm encryption and RFC 8292 VAPID using the platform cryptography APIs.</summary>
public sealed class WebPushTransport(Config config, IHttpClientFactory http)
{
    internal const string RateLimitPolicy = "web-push";

    public bool IsConfigured
    {
        get
        {
            try
            {
                using var key = SigningKey();
                using var publicKey = ECDsa.Create(PublicParameters(Decode(config.WebPushVapidPublicKey ?? "")));
                var probe = "web-push"u8;
                var signature = key.SignData(probe, HashAlgorithmName.SHA256);
                return publicKey.VerifyData(probe, signature, HashAlgorithmName.SHA256)
                    && Uri.TryCreate(config.WebPushVapidSubject, UriKind.Absolute, out var subject)
                    && (subject.Scheme == "mailto" || subject.Scheme == "https");
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException) { return false; }
        }
    }

    public string? PublicKey => IsConfigured ? config.WebPushVapidPublicKey : null;

    internal static bool IsValid(WebPushSubscription? subscription)
    {
        if (subscription?.Keys is null || !IsAllowedEndpoint(subscription.Endpoint)) return false;
        try
        {
            var publicKey = Decode(subscription.Keys.P256dh);
            if (publicKey.Length != 65 || publicKey[0] != 4 || Decode(subscription.Keys.Auth).Length != 16) return false;
            using var key = ECDiffieHellman.Create(PublicParameters(publicKey));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException) { return false; }
    }

    // Anonymous clients must never turn the sender into an arbitrary HTTP client (including redirects).
    internal static bool IsAllowedEndpoint(string? endpoint)
    {
        if (endpoint is null || endpoint.Length > 2048 || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        var host = uri.IdnHost;
        return host == "fcm.googleapis.com" || host == "updates.push.services.mozilla.com"
            || host == "updates-autopush.prod.mozaws.net" || host == "web.push.apple.com"
            || host.EndsWith(".push.apple.com", StringComparison.Ordinal)
            || host.EndsWith(".notify.windows.com", StringComparison.Ordinal);
    }

    internal async Task<WebPushResult> SendAsync(WebPushSubscription subscription, string payload, string topic,
        DateTimeOffset now, CancellationToken ct)
    {
        if (!IsValid(subscription)) throw new ArgumentException("Invalid push subscription.");
        var bytes = Encoding.UTF8.GetBytes(payload);
        if (bytes.Length > 3000) throw new ArgumentException("Push payload exceeds its byte budget.");
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var body = Encrypt(bytes, Decode(subscription.Keys.P256dh), Decode(subscription.Keys.Auth),
            ephemeral, RandomNumberGenerator.GetBytes(16));
        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Endpoint);
        request.Headers.TryAddWithoutValidation("Authorization", Authorization(new Uri(subscription.Endpoint), now));
        request.Headers.TryAddWithoutValidation("TTL", "86400");
        request.Headers.TryAddWithoutValidation("Urgency", "normal");
        request.Headers.TryAddWithoutValidation("Topic", topic);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        using var response = await http.CreateClient(nameof(WebPushTransport)).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - now);
        return new(response.StatusCode, retry);
    }

    internal string Authorization(Uri endpoint, DateTimeOffset now)
    {
        var header = Encode("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"u8.ToArray());
        var claims = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            aud = endpoint.GetLeftPart(UriPartial.Authority), exp = now.AddHours(12).ToUnixTimeSeconds(), sub = config.WebPushVapidSubject
        }));
        var unsigned = header + "." + claims;
        using var key = SigningKey();
        var signature = key.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"vapid t={unsigned}.{Encode(signature)}, k={config.WebPushVapidPublicKey}";
    }

    private ECDsa SigningKey()
    {
        var parameters = PublicParameters(Decode(config.WebPushVapidPublicKey ?? ""));
        parameters.D = Decode(config.WebPushVapidPrivateKey ?? "");
        if (parameters.D.Length != 32) throw new CryptographicException("Invalid VAPID key.");
        return ECDsa.Create(parameters);
    }

    internal static ECParameters PublicParameters(byte[] key)
    {
        if (key.Length != 65 || key[0] != 4) throw new CryptographicException("Invalid P-256 public key.");
        return new() { Curve = ECCurve.NamedCurves.nistP256, Q = new() { X = key[1..33], Y = key[33..65] } };
    }

    internal static byte[] PublicBytes(ECParameters key) => [4, .. key.Q.X!, .. key.Q.Y!];
    internal static string Encode(byte[] value) => WebEncoders.Base64UrlEncode(value);
    internal static byte[] Decode(string value) => WebEncoders.Base64UrlDecode(value);

    internal static byte[] Encrypt(byte[] plaintext, byte[] recipientPublic, byte[] auth, ECDiffieHellman sender, byte[] salt)
    {
        using var recipient = ECDiffieHellman.Create(PublicParameters(recipientPublic));
        var senderPublic = PublicBytes(sender.ExportParameters(false));
        var shared = sender.DeriveRawSecretAgreement(recipient.PublicKey);
        byte[] info = [.. "WebPush: info\0"u8, .. recipientPublic, .. senderPublic];
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, auth, info);
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, "Content-Encoding: nonce\0"u8.ToArray());
        byte[] padded = [.. plaintext, 2];
        var result = new byte[86 + padded.Length + 16];
        salt.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(16, 4), 4096);
        result[20] = 65;
        senderPublic.CopyTo(result, 21);
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, padded, result.AsSpan(86, padded.Length), result.AsSpan(86 + padded.Length, 16));
        CryptographicOperations.ZeroMemory(shared);
        CryptographicOperations.ZeroMemory(ikm);
        CryptographicOperations.ZeroMemory(key);
        return result;
    }
}
