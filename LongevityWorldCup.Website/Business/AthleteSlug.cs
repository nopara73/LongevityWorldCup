namespace LongevityWorldCup.Website.Business;

internal static class AthleteSlug
{
    internal static string Normalize(string? slug)
    {
        return (slug ?? "").Trim().Replace('-', '_').ToLowerInvariant();
    }

    internal static string ToDisplayName(string slug)
    {
        var parts = Normalize(slug).Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return slug;
        return string.Join(" ", parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
