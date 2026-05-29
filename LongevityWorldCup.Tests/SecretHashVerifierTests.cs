using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public class SecretHashVerifierTests
{
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
}
