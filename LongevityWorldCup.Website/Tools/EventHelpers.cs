using System.Globalization;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Tools;

public static class EventHelpers
{
    public static bool TryExtractSlug(string raw, out string slug) => TryExtractField(raw, "slug", out slug);

    public static bool TryExtractBadgeLabel(string raw, out string label) => TryExtractField(raw, "badge", out label);

    public static bool TryExtractCustomEventTitle(string? rawText, out string title)
    {
        title = string.Empty;
        if (string.IsNullOrEmpty(rawText)) return false;

        var nl = rawText.IndexOf('\n');
        var cr = rawText.IndexOf('\r');

        int i;
        if (nl >= 0 && cr >= 0) i = Math.Min(nl, cr);
        else i = Math.Max(nl, cr);

        if (i < 0) i = rawText.Length;

        title = rawText.Substring(0, i).Trim();
        return title.Length > 0;
    }

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

    public static bool TryExtractPrevs(string raw, out string[] prevs) => TryExtractCsvList(raw, "prevs", out prevs);

    public static bool TryExtractSolo(string raw, out bool solo) => TryExtractFlag(raw, "solo", out solo);

    public static bool TryExtractCategory(string raw, out string category) => TryExtractField(raw, "cat", out category);

    public static bool TryExtractValue(string raw, out string value) => TryExtractField(raw, "val", out value);

    public static bool TryExtractTx(string raw, out string tx) => TryExtractField(raw, "tx", out tx);

    public static bool TryExtractSats(string raw, out long sats)
    {
        sats = 0;
        if (!TryExtractField(raw, "sats", out var s)) return false;
        return long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out sats);
    }

    public static bool TryExtractAthleteCount(string raw, out int count)
    {
        count = 0;
        if (!TryExtractField(raw, "athletes", out var s)) return false;
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
    }

    public static string NormalizeBadgeLabel(string? label) =>
        (label ?? string.Empty).Replace('–', '-').Trim();

    public static string ExtractDomainFromLabel(string? label)
    {
        var s = NormalizeBadgeLabel(label);
        if (string.IsNullOrEmpty(s)) return "Domain";
        var i = s.IndexOf('-', StringComparison.Ordinal);
        if (i < 0 || i + 1 >= s.Length) return "Domain";
        var rest = s[(i + 1)..].Trim();
        return string.IsNullOrEmpty(rest) ? "Domain" : rest;
    }

    static bool TryExtractField(string raw, string field, out string value)
    {
        var m = Regex.Match(raw ?? string.Empty, $@"\b{Regex.Escape(field)}\[(.*?)\]", RegexOptions.CultureInvariant);
        value = m.Success ? m.Groups[1].Value : string.Empty;
        return m.Success;
    }

    static bool TryExtractFlag(string raw, string field, out bool flag)
    {
        flag = false;
        if (!TryExtractField(raw, field, out var s)) return false;

        if (TryParseFlagValue(s, out flag)) return true;

        if (string.IsNullOrWhiteSpace(s))
        {
            flag = true;
            return true;
        }

        return false;
    }

    static bool TryParseFlagValue(string s, out bool flag)
    {
        flag = false;
        if (string.IsNullOrWhiteSpace(s)) return false;

        s = s.Trim();
        if (s == "1")
        {
            flag = true;
            return true;
        }

        if (s == "0")
        {
            flag = false;
            return true;
        }

        return bool.TryParse(s, out flag);
    }

    static bool TryExtractCsvList(string raw, string field, out string[] values)
    {
        if (!TryExtractField(raw, field, out var s))
        {
            values = Array.Empty<string>();
            return false;
        }

        values = SplitCsv(s);
        return values.Length > 0;
    }

    static string[] SplitCsv(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Array.Empty<string>();
        var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts : Array.Empty<string>();
    }
}
