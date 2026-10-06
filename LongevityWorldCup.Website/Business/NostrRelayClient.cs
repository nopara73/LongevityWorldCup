using System.Net.WebSockets;
using System.Text.Json;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public interface INostrRelayTransport
{
    Task PublishAndVerifyAsync(Uri relay, NostrEvent item, CancellationToken ct);
}

public sealed class NostrRelayException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed class NostrRelayTransport : INostrRelayTransport
{
    internal const int MaxFrameBytes = 128 * 1024;
    private readonly Func<Uri, CancellationToken, Task<WebSocket>> _connect;

    public NostrRelayTransport() : this(async (uri, ct) =>
    {
        var socket = new ClientWebSocket();
        try { await socket.ConnectAsync(uri, ct); return socket; }
        catch { socket.Dispose(); throw; }
    }) { }

    internal NostrRelayTransport(Func<Uri, CancellationToken, Task<WebSocket>> connect) => _connect = connect;

    public async Task PublishAndVerifyAsync(Uri relay, NostrEvent item, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var socket = await _connect(relay, timeout.Token);
            await SendAsync(socket, ["EVENT", item], timeout.Token);
            while (true)
            {
                using var message = await ReceiveAsync(socket, timeout.Token);
                var data = message.RootElement;
                if (IsMessage(data, "AUTH")) throw new NostrRelayException("RelayAuthRequired");
                if (!IsMessage(data, "OK") || data.GetArrayLength() < 3 || data[1].ValueKind != JsonValueKind.String
                    || data[1].GetString() != item.Id) continue;
                if (data[2].ValueKind != JsonValueKind.True) throw new NostrRelayException("RelayRejected");
                break;
            }

            // An OK receipt alone is insufficient. Retrieve and cryptographically verify the
            // same event from this relay before recording successful delivery.
            var subscription = "lwc-" + Guid.NewGuid().ToString("N");
            await SendAsync(socket, ["REQ", subscription, new { ids = new[] { item.Id }, limit = 1 }], timeout.Token);
            while (true)
            {
                using var message = await ReceiveAsync(socket, timeout.Token);
                var data = message.RootElement;
                if (IsMessage(data, "AUTH")) throw new NostrRelayException("RelayAuthRequired");
                if (data.GetArrayLength() < 2 || data[1].ValueKind != JsonValueKind.String || data[1].GetString() != subscription) continue;
                if (IsMessage(data, "EOSE") || IsMessage(data, "CLOSED")) throw new NostrRelayException("RelayReadbackMissing");
                if (!IsMessage(data, "EVENT") || data.GetArrayLength() < 3) continue;
                var received = data[2].Deserialize<NostrEvent>();
                if (received?.Id == item.Id && NostrProtocol.Verify(received, item.PublicKey)) return;
                throw new NostrRelayException("RelayReadbackInvalid");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new NostrRelayException("RelayTimeout"); }
        catch (Exception ex) when (ex is WebSocketException or JsonException) { throw new NostrRelayException("RelayProtocolOrConnection"); }
    }

    private static bool IsMessage(JsonElement data, string type) => data.ValueKind == JsonValueKind.Array
        && data.GetArrayLength() > 0 && data[0].ValueKind == JsonValueKind.String && data[0].GetString() == type;

    private static async Task SendAsync(WebSocket socket, object[] message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        if (bytes.Length > MaxFrameBytes) throw new NostrRelayException("RelayFrameTooLarge");
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, ct);
    }

    private static async Task<JsonDocument> ReceiveAsync(WebSocket socket, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var result = await socket.ReceiveAsync(chunk.AsMemory(), ct);
            if (result.MessageType != WebSocketMessageType.Text) throw new NostrRelayException("RelayClosedOrBinary");
            if (buffer.Length + result.Count > MaxFrameBytes) throw new NostrRelayException("RelayFrameTooLarge");
            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        var document = JsonDocument.Parse(buffer.ToArray());
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            document.Dispose();
            throw new NostrRelayException("RelayInvalidMessage");
        }
        return document;
    }
}

public sealed class NostrRelayClient(Config config, INostrRelayTransport transport)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config.NostrPrivateKeyHex)
        && !string.IsNullOrWhiteSpace(config.NostrPublicKeyHex) && config.NostrRelayUrls is { Length: > 0 };

    internal string VerifyAccount()
    {
        try
        {
            if (!IsConfigured || NostrProtocol.GetPublicKey(config.NostrPrivateKeyHex!) != config.NostrPublicKeyHex)
                throw new NostrRelayException("NostrAccountMismatch");
            return config.NostrPublicKeyHex!;
        }
        catch (ArgumentException) { throw new NostrRelayException("NostrInvalidKey"); }
    }

    internal NostrEvent Prepare(string content, DateTimeOffset now, int kind = 1, string[][]? tags = null)
        => NostrProtocol.Sign(config.NostrPrivateKeyHex!, VerifyAccount(), content, now.ToUnixTimeSeconds(), kind, tags);

    internal async Task<SocialPostReceipt> PublishAsync(NostrEvent item, CancellationToken ct)
    {
        var identity = VerifyAccount();
        if (item.Kind != 1 || !NostrProtocol.Verify(item, identity)) throw new NostrRelayException("NostrInvalidPreparedEvent");
        var relays = config.NostrRelayUrls.Select(ParseRelay).DistinctBy(x => x.AbsoluteUri).ToArray();
        if (relays.Length < 2 || relays.Length > 8) throw new NostrRelayException("NostrRequiresTwoToEightRelays");
        var results = await Task.WhenAll(relays.Select(async relay =>
        {
            try { await transport.PublishAndVerifyAsync(relay, item, ct); return true; }
            catch (Exception) when (!ct.IsCancellationRequested) { return false; }
        }));
        if (results.Count(x => x) < 2) throw new NostrRelayException("NostrInsufficientRelayReceipts");
        return new(item.Id, "nostr:" + NostrProtocol.EncodeIdentifier(item.Id, "note"));
    }

    private static Uri ParseRelay(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "wss" || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new NostrRelayException("NostrInvalidRelayUrl");
        return uri;
    }
}
