using System.Collections.Frozen;
using System.Text.Json;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

internal static class FlagIconCatalog
{
    private static readonly FrozenDictionary<string, string> Codes = LoadCodes();

    public static string? GetCode(string canonicalName) => Codes.GetValueOrDefault(FlagCanonicalizer.NormalizeKey(canonicalName));

    private static FrozenDictionary<string, string> LoadCodes()
    {
        using var stream = typeof(FlagIconCatalog).Assembly.GetManifestResourceStream("flag-icon-codes.json")
            ?? throw new InvalidOperationException("The embedded flag icon catalog is missing.");
        var codes = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("The embedded flag icon catalog is empty.");
        return codes.ToFrozenDictionary(entry => FlagCanonicalizer.NormalizeKey(entry.Key), entry => entry.Value, StringComparer.Ordinal);
    }
}
