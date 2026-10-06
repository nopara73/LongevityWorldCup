using System.Security.Cryptography;
using System.Text;
using LongevityWorldCup.Website.Tools;
using NBitcoin.Secp256k1;
using Xunit;
using SHA256 = System.Security.Cryptography.SHA256;

namespace LongevityWorldCup.Tests;

public sealed class NostrProtocolTests
{
    // Published BIP-340 vector 0. These keys are test data, never an account.
    internal const string TestPrivateKey = "0000000000000000000000000000000000000000000000000000000000000003";
    internal const string TestPublicKey = "f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9";

    [Fact]
    public void Signing_UsesThePublishedBip340Vector()
    {
        using var key = ECPrivKey.Create(Convert.FromHexString(TestPrivateKey));
        var signature = new byte[64];
        key.SignBIP340(new byte[32], new byte[32].AsMemory()).WriteToSpan(signature);
        Assert.Equal("e907831f80848d1069a5371b402410364bdf1c5f8307b0084c55f1ce2dca821525f66a4a85ea8b71e482a74f382d2ce5ebeee8fdb2172f477df4900d310536c0",
            Convert.ToHexStringLower(signature));
        Assert.Equal(TestPublicKey, NostrProtocol.GetPublicKey(TestPrivateKey));
    }

    [Theory]
    [InlineData("3bf0c63fcb93463407af97a5e5ee64fa883d107ef9e558472c4eb9aaaefa459d", "npub", "npub180cvv07tjdrrgpa0j7j7tmnyl2yr6yr7l8j4s3evf6u64th6gkwsyjh6w6")]
    [InlineData("7e7e9c42a91bfef19fa929e5fda1b72e0ebc1a4c1141673e2794234d86addf4e", "npub", "npub10elfcs4fr0l0r8af98jlmgdh9c8tcxjvz9qkw038js35mp4dma8qzvjptg")]
    [InlineData("67dea2ed018072d675f5415ecfaed7d2597555e202d85b3d65ea4e58d2d92ffa", "nsec", "nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe5")]
    public void Identifiers_MatchPublishedNip19Vectors(string hex, string prefix, string encoded)
        => Assert.Equal(encoded, NostrProtocol.EncodeIdentifier(hex, prefix));

    [Fact]
    public void CanonicalSerialization_PreservesLiteralUnicodeAndRequiredEscapes()
    {
        const string content = "A 🏆 <&> / 零\u2028\u2029\n\t\r\b\f\"\\\u0001";
        var item = NostrProtocol.Sign(TestPrivateKey, TestPublicKey, content, 123, tags: [["t", "longevity"]]);
        var canonical = "[0,\"" + TestPublicKey + "\",123,1,[[\"t\",\"longevity\"]],\"A 🏆 <&> / 零\u2028\u2029\\n\\t\\r\\b\\f\\\"\\\\\\u0001\"]";
        Assert.Equal(canonical, NostrProtocol.CanonicalJson(item));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))), item.Id);
        Assert.True(NostrProtocol.Verify(item, TestPublicKey));
        Assert.False(NostrProtocol.Verify(item with { Content = content + "!" }, TestPublicKey));
        Assert.False(NostrProtocol.Verify(item with { CreatedAt = 124 }, TestPublicKey));
        Assert.False(NostrProtocol.Verify(item with { Tags = [["t", "another-topic"]] }, TestPublicKey));
        Assert.False(NostrProtocol.Verify(item with { Signature = new string('0', 128) }, TestPublicKey));
    }

    [Fact]
    public void MalformedKeysOrUnicode_AreRejectedWithoutReturningSecretValues()
    {
        Assert.Equal("InvalidNostrPrivateKey", Assert.Throws<ArgumentException>(() => NostrProtocol.GetPublicKey(new string('0', 64))).Message);
        Assert.Equal("NostrAccountMismatch", Assert.Throws<InvalidOperationException>(() => NostrProtocol.Sign(TestPrivateKey, new string('1', 64), "Hello", 123)).Message);
        Assert.Throws<EncoderFallbackException>(() => NostrProtocol.Sign(TestPrivateKey, TestPublicKey, "\ud800", 123));
        var valid = NostrProtocol.Sign(TestPrivateKey, TestPublicKey, "Hello", 123);
        Assert.False(NostrProtocol.Verify(valid with { Content = null! }, TestPublicKey));
        Assert.False(NostrProtocol.Verify(valid with { Tags = [null!] }, TestPublicKey));
    }
}
