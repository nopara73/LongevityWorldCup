using System.Globalization;
using System.Text;

namespace LongevityWorldCup.Website.Business;

public static class FlagCanonicalizer
{
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["america"] = "United States",
            ["brasil"] = "Brazil",
            ["magyarorszag"] = "Hungary",
            ["nld"] = "Netherlands",
            ["nz"] = "New Zealand",
            ["nzd"] = "New Zealand",
            ["slovak republic"] = "Slovakia",
            ["svk"] = "Slovakia",
            ["turkiye"] = "Turkey",
            ["u s"] = "United States",
            ["u s a"] = "United States",
            ["united states of america"] = "United States",
            ["us"] = "United States",
            ["usa"] = "United States"
        };

    public static string GetCanonicalName(string? flag)
    {
        var cleaned = NormalizeWhitespace(flag);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        return Aliases.TryGetValue(NormalizeKey(cleaned), out var alias)
            ? alias
            : cleaned;
    }

    private static string NormalizeKey(string? value)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var pendingSeparator = false;

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(char.ToLowerInvariant(c));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeWhitespace(string? value)
        => string.Join(' ', (value ?? string.Empty).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
