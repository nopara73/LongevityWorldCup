using System.Globalization;
using System.Text;

namespace LongevityWorldCup.Website.Business;

public static class FlagRouteCatalog
{
    public static IReadOnlyList<FlagRouteInfo> BuildRoutes(IEnumerable<string?> flags)
    {
        return flags
            .Select(TryCreate)
            .Where(route => route is not null)
            .Cast<FlagRouteInfo>()
            .GroupBy(route => route.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(route => route.Name, StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(route => route.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool TryResolve(string rawSlug, IEnumerable<string?> flags, out FlagRouteInfo route)
    {
        var decodedSlug = Uri.UnescapeDataString((rawSlug ?? string.Empty).Trim());
        var normalizedSlug = NormalizeRouteSlug(FlagCanonicalizer.GetCanonicalName(decodedSlug));
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            route = default!;
            return false;
        }

        route = BuildRoutes(flags).FirstOrDefault(candidate =>
            string.Equals(candidate.Slug, normalizedSlug, StringComparison.OrdinalIgnoreCase))!;
        return route is not null;
    }

    public static bool TryCreate(string? flag, out FlagRouteInfo route)
    {
        route = TryCreate(flag)!;
        return route is not null;
    }

    private static FlagRouteInfo? TryCreate(string? flag)
    {
        var name = FlagCanonicalizer.GetCanonicalName(flag);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var slug = NormalizeRouteSlug(name);
        return string.IsNullOrWhiteSpace(slug)
            ? null
            : new FlagRouteInfo(name, slug, $"/flag/{slug}");
    }

    private static string NormalizeRouteSlug(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var pendingSeparator = false;

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(c));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}

public sealed record FlagRouteInfo(string Name, string Slug, string Path);
