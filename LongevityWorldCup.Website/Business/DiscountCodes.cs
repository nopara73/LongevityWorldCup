namespace LongevityWorldCup.Website.Business;

public static class DiscountCodes
{
    public const string MightyKlaus = "mightyklaus";
    public const decimal MightyKlausPercent = 40m;

    public static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        return string.Equals(trimmed, MightyKlaus, StringComparison.OrdinalIgnoreCase)
            ? MightyKlaus
            : null;
    }
}
