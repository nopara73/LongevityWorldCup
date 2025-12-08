using System.Globalization;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Tools;

public static class EventHelpers
{
    public static bool TryExtractSlug(string raw, out string slug) => TryExtractField(raw, "slug", out slug);

    public static bool TryExtractBadgeLabel(string raw, out string label) => TryExtractField(raw, "badge", out label);

    public static bool TryExtractRank(string raw, out int rank)
    {
        rank = 0;
        if (!TryExtractField(raw, "rank", out var s)) return false;
        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out rank);
    }

    public static bool TryExtractPlace(string raw, out int place)
    {
        place = 0;
        if (!TryExtractField(raw, "place", out var s)) return false;
        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out place);
    }

    public static bool TryExtractPrev(string raw, out string prev) => TryExtractField(raw, "prev", out prev);

    public static bool TryExtractCategory(string raw, out string category) => TryExtractField(raw, "cat", out category);

    public static bool TryExtractValue(string raw, out string value) => TryExtractField(raw, "val", out value);

    static bool TryExtractField(string raw, string field, out string value)
    {
        var m = Regex.Match(raw ?? string.Empty, $@"\b{Regex.Escape(field)}\[(.*?)\]", RegexOptions.CultureInvariant);
        value = m.Success ? m.Groups[1].Value : string.Empty;
        return m.Success;
    }
}