using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public class SecretHashVerifierTests
{
    [Fact]
    public void CreateHash_RejectsEmptySecret()
    {
        Assert.Throws<ArgumentException>(() => SecretHashVerifier.CreateHash(""));
    }

    [Fact]
    public void CreateHash_VerifiesMatchingSecret()
    {
        var hash = SecretHashVerifier.CreateHash("correct horse battery staple");

        var result = SecretHashVerifier.Verify("correct horse battery staple", hash);

        Assert.Equal(SecretVerificationResult.Verified, result);
    }

    [Fact]
    public void Verify_AcceptsBrowserWebCryptoHashFormat()
    {
        const string browserGeneratedHash = "pbkdf2-sha256:210000:AAECAwQFBgcICQoLDA0ODw==:S2Nb9dGpdHVFr+Ve1cxBYyXfCBotB9wl2rjY0IN3X2w=";

        var result = SecretHashVerifier.Verify("browser-style secret", browserGeneratedHash);

        Assert.Equal(SecretVerificationResult.Verified, result);
    }

    [Fact]
    public void Verify_RejectsWrongSecret()
    {
        var hash = SecretHashVerifier.CreateHash("correct horse battery staple");

        var result = SecretHashVerifier.Verify("wrong secret", hash);

        Assert.Equal(SecretVerificationResult.Mismatch, result);
    }

    [Fact]
    public void Verify_DistinguishesMissingAndInvalidConfig()
    {
        Assert.Equal(SecretVerificationResult.NotConfigured, SecretHashVerifier.Verify("secret", ""));
        Assert.Equal(SecretVerificationResult.InvalidHash, SecretHashVerifier.Verify("secret", "not-a-valid-hash"));
    }

    [Fact]
    public void Verify_RejectsWeakHashParameters()
    {
        var weak = "pbkdf2-sha256:1:AQID:BAUG";

        var result = SecretHashVerifier.Verify("secret", weak);

        Assert.Equal(SecretVerificationResult.InvalidHash, result);
    }

    [Fact]
    public void Verify_RejectsInvalidBase64Payloads()
    {
        var validSalt = Convert.ToBase64String(new byte[16]);
        var validHash = Convert.ToBase64String(new byte[32]);

        Assert.Equal(
            SecretVerificationResult.InvalidHash,
            SecretHashVerifier.Verify("secret", $"{SecretHashVerifier.Algorithm}:{SecretHashVerifier.DefaultIterations}:not-base64:{validHash}"));
        Assert.Equal(
            SecretVerificationResult.InvalidHash,
            SecretHashVerifier.Verify("secret", $"{SecretHashVerifier.Algorithm}:{SecretHashVerifier.DefaultIterations}:{validSalt}:not-base64"));
    }

    [Fact]
    public void Verify_RejectsShortSaltOrHashPayloads()
    {
        var validSalt = new byte[16];
        var validHash = new byte[32];

        Assert.Equal(
            SecretVerificationResult.InvalidHash,
            SecretHashVerifier.Verify("secret", BuildStoredHash(new byte[15], validHash)));
        Assert.Equal(
            SecretVerificationResult.InvalidHash,
            SecretHashVerifier.Verify("secret", BuildStoredHash(validSalt, new byte[31])));
    }

    [Fact]
    public void Verify_RejectsOversizedHashPayload()
    {
        var result = SecretHashVerifier.Verify("secret", BuildStoredHash(new byte[16], new byte[129]));

        Assert.Equal(SecretVerificationResult.InvalidHash, result);
    }

    private static string BuildStoredHash(byte[] salt, byte[] hash)
    {
        return $"{SecretHashVerifier.Algorithm}:{SecretHashVerifier.DefaultIterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }
}
