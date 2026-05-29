using System.Security.Cryptography;
using System.Text;

namespace LongevityWorldCup.Website.Tools;

public enum SecretVerificationResult
{
    Verified,
    Mismatch,
    NotConfigured,
    InvalidHash
}

public static class SecretHashVerifier
{
    public const string Algorithm = "pbkdf2-sha256";
    public const int DefaultIterations = 210_000;
    private const int SaltByteCount = 16;
    private const int HashByteCount = 32;
    private const int MaxHashByteCount = 128;

    public static string CreateHash(string secret)
    {
        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("Secret is required.", nameof(secret));

        var salt = RandomNumberGenerator.GetBytes(SaltByteCount);
        var hash = DeriveHash(secret, salt, DefaultIterations, HashByteCount);
        return $"{Algorithm}:{DefaultIterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static SecretVerificationResult Verify(string? secret, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
            return SecretVerificationResult.NotConfigured;

        if (string.IsNullOrEmpty(secret))
            return SecretVerificationResult.Mismatch;

        var parts = storedHash.Split(':');
        if (parts.Length != 4 || !string.Equals(parts[0], Algorithm, StringComparison.Ordinal))
            return SecretVerificationResult.InvalidHash;

        if (!int.TryParse(parts[1], out var iterations) || iterations < DefaultIterations)
            return SecretVerificationResult.InvalidHash;

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return SecretVerificationResult.InvalidHash;
        }

        if (salt.Length < SaltByteCount || expectedHash.Length < HashByteCount || expectedHash.Length > MaxHashByteCount)
            return SecretVerificationResult.InvalidHash;

        var actualHash = DeriveHash(secret, salt, iterations, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash)
            ? SecretVerificationResult.Verified
            : SecretVerificationResult.Mismatch;
    }

    private static byte[] DeriveHash(string secret, byte[] salt, int iterations, int hashByteCount)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(secret),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            hashByteCount);
    }
}
