using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class NostrRelayTests
{
    [Fact]
    public async Task RelayReceipts_RequireMatchingAcceptanceAndSignedReadbackAcrossFragments()
    {
        var item = Note();
        using var socket = new RelaySocket(item, "duplicate");
        var transport = new NostrRelayTransport((_, _) => Task.FromResult<WebSocket>(socket));
        await transport.PublishAndVerifyAsync(new Uri("wss://relay.example"), item, default);
        Assert.Equal(JsonSerializer.Serialize(item), JsonSerializer.Serialize(Assert.Single(socket.Writes)));
        Assert.True(socket.WasDisposed);
    }

    [Theory]
    [InlineData("rejected", "RelayRejected")]
    [InlineData("auth", "RelayAuthRequired")]
    [InlineData("missing", "RelayReadbackMissing")]
    [InlineData("tampered", "RelayReadbackInvalid")]
    [InlineData("oversized", "RelayFrameTooLarge")]
    public async Task IncompleteOrInvalidReceipts_DoNotFinishDelivery(string behavior, string code)
    {
        var item = Note();
        using var socket = new RelaySocket(item, behavior);
        var transport = new NostrRelayTransport((_, _) => Task.FromResult<WebSocket>(socket));
        Assert.Equal(code, (await Assert.ThrowsAsync<NostrRelayException>(() => transport.PublishAndVerifyAsync(new Uri("wss://relay.example"), item, default))).Code);
        Assert.True(socket.WasDisposed);
    }

    [Fact]
    public async Task RedundantRelays_RequireTwoReceiptsAndRejectWrongKeysBeforeWriting()
    {
        var transport = new RecordingRelays { FailSecond = true };
        var config = Configured();
        var client = new NostrRelayClient(config, transport);
        var item = client.Prepare("Hello", DateTimeOffset.UtcNow);
        Assert.Equal("NostrInsufficientRelayReceipts", (await Assert.ThrowsAsync<NostrRelayException>(() => client.PublishAsync(item, default))).Code);
        transport.FailSecond = false;
        var receipt = await client.PublishAsync(item, default);
        Assert.Equal(item.Id, receipt.Id);
        Assert.StartsWith("nostr:note1", receipt.Url);
        Assert.Equal(4, transport.Writes.Count);
        Assert.All(transport.Writes, write => Assert.Equal(item, write.Item));
        config.NostrPublicKeyHex = new string('1', 64);
        Assert.Equal("NostrAccountMismatch", (await Assert.ThrowsAsync<NostrRelayException>(() => client.PublishAsync(item, default))).Code);
        Assert.Equal(4, transport.Writes.Count);
    }

    [Theory]
    [InlineData("ws://insecure.example")]
    [InlineData("wss://user:password@relay.example")]
    [InlineData("wss://relay.example?token=secret")]
    public async Task RelayConfiguration_RejectsInsecureOrCredentialBearingUrls(string url)
    {
        var transport = new RecordingRelays();
        var config = Configured();
        config.NostrRelayUrls = [url, "wss://other.example"];
        var client = new NostrRelayClient(config, transport);
        Assert.Equal("NostrInvalidRelayUrl", (await Assert.ThrowsAsync<NostrRelayException>(() => client.PublishAsync(Note(), default))).Code);
        Assert.Empty(transport.Writes);
    }

    internal static Config Configured() => new()
    {
        NostrPrivateKeyHex = NostrProtocolTests.TestPrivateKey, NostrPublicKeyHex = NostrProtocolTests.TestPublicKey,
        NostrRelayUrls = ["wss://first.example", "wss://second.example"]
    };

    private static NostrEvent Note() => NostrProtocol.Sign(NostrProtocolTests.TestPrivateKey, NostrProtocolTests.TestPublicKey, "A sport for time 🏆", 123);

    internal sealed class RecordingRelays : INostrRelayTransport
    {
        public bool FailSecond { get; set; }
        public List<(Uri Relay, NostrEvent Item)> Writes { get; } = [];
        public Func<CancellationToken, Task>? BeforeReceipt { get; set; }
        public async Task PublishAndVerifyAsync(Uri relay, NostrEvent item, CancellationToken ct)
        {
            lock (Writes) Writes.Add((relay, item));
            if (BeforeReceipt is not null) await BeforeReceipt(ct);
            if (FailSecond && relay.Host == "second.example") throw new NostrRelayException("RelayReplyLost");
        }
    }

    private sealed class RelaySocket(NostrEvent item, string behavior) : WebSocket
    {
        private readonly Queue<byte[]> _messages = new();
        private byte[]? _current;
        private int _offset;
        public List<NostrEvent> Writes { get; } = [];
        public bool WasDisposed { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose() => WasDisposed = true;

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken ct)
        {
            using var document = JsonDocument.Parse(buffer.AsMemory());
            var message = document.RootElement;
            if (message[0].GetString() == "EVENT")
            {
                Writes.Add(message[1].Deserialize<NostrEvent>()!);
                Enqueue(["OK", new string('0', 64), true, "Receipt for a different event"]);
                if (behavior == "auth") Enqueue(["AUTH", "challenge"]);
                else if (behavior == "oversized") Enqueue(["NOTICE", new string('x', NostrRelayTransport.MaxFrameBytes)]);
                else Enqueue(["OK", item.Id, behavior != "rejected", "duplicate: already have this event"]);
            }
            else if (message[0].GetString() == "REQ")
            {
                var subscription = message[1].GetString()!;
                Assert.Equal(item.Id, message[2].GetProperty("ids")[0].GetString());
                if (behavior != "missing") Enqueue(["EVENT", subscription, behavior == "tampered" ? item with { Content = "Changed after signing" } : item]);
                Enqueue(["EOSE", subscription]);
            }
            return Task.CompletedTask;
        }

        private void Enqueue(object[] message) => _messages.Enqueue(JsonSerializer.SerializeToUtf8Bytes(message));

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _current ??= _messages.Dequeue();
            var count = Math.Min(Math.Min(buffer.Count, 37), _current.Length - _offset);
            _current.AsSpan(_offset, count).CopyTo(buffer.AsSpan());
            _offset += count;
            var end = _offset == _current.Length;
            if (end) { _current = null; _offset = 0; }
            return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Text, end));
        }
    }
}
