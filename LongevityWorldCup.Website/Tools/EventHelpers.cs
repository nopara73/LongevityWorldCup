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

    public static bool TryExtractPreviousPlace(string raw, out int previousPlace)
    {
        previousPlace = 0;
        if (!TryExtractField(raw, "prevPlace", out var s)) return false;
        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out previousPlace);
    }

    public static bool TryExtractPrev(string raw, out string prev) => TryExtractField(raw, "prev", out prev);

    public static bool TryExtractPrevs(string raw, out string[] prevs) => TryExtractCsvList(raw, "prevs", out prevs);

    public static bool TryExtractSolo(string raw, out bool solo) => TryExtractFlag(raw, "solo", out solo);

    public static bool TryExtractCategory(string raw, out string category) => TryExtractField(raw, "cat", out category);

    public static bool TryExtractValue(string raw, out string value) => TryExtractField(raw, "val", out value);

    public static bool TryExtractLeague(string raw, out string league) => TryExtractField(raw, "league", out league);

    public static bool TryExtractClock(string raw, out string clock) => TryExtractField(raw, "clock", out clock);

    public static bool TryExtractFromAge(string raw, out double age) => TryExtractDoubleField(raw, "from", out age);

    public static bool TryExtractToAge(string raw, out double age) => TryExtractDoubleField(raw, "to", out age);

    public static bool TryExtractCrowdAge(string raw, out double crowdAge) => TryExtractDoubleField(raw, "crowdAge", out crowdAge);

    public static bool TryExtractImprovement(string raw, out double improvement) => TryExtractDoubleField(raw, "improvement", out improvement);

    public static bool TryExtractAgeReduction(string raw, out double ageReduction) => TryExtractDoubleField(raw, "ageReduction", out ageReduction);

    public static bool TryExtractCrowdCount(string raw, out int crowdCount)
    {
        crowdCount = 0;
        if (!TryExtractField(raw, "crowdCount", out var s)) return false;
        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out crowdCount);
    }

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

    public static bool TryExtractDomain(string raw, out string domain) => TryExtractField(raw, "domain", out domain);

    public static bool TryExtractBiologicalAgeImprovement(
        string raw,
        out string clock,
        out double fromAge,
        out double toAge)
    {
        clock = string.Empty;
        fromAge = 0;
        toAge = 0;

        if (!TryExtractClock(raw, out var rawClock)) return false;
        var normalizedClock = NormalizeClock(rawClock);
        if (normalizedClock is null) return false;
        if (!TryExtractFromAge(raw, out fromAge)) return false;
        if (!TryExtractToAge(raw, out toAge)) return false;
        if (!double.IsFinite(fromAge) || !double.IsFinite(toAge)) return false;
        if (toAge >= fromAge) return false;

        clock = normalizedClock;
        return true;
    }

    public static bool TryExtractCrowdAgeTop10Change(
        string raw,
        out int place,
        out int? previousPlace,
        out double crowdAge,
        out int crowdCount)
    {
        place = 0;
        previousPlace = null;
        crowdAge = 0;
        crowdCount = 0;

        if (!TryExtractPlace(raw, out place) || place is < 1 or > 10) return false;

        if (TryExtractPreviousPlace(raw, out var parsedPreviousPlace))
        {
            if (parsedPreviousPlace is < 1 or > 10) return false;
            previousPlace = parsedPreviousPlace;
        }

        if (!TryExtractCrowdAge(raw, out crowdAge) || !double.IsFinite(crowdAge)) return false;
        if (!TryExtractCrowdCount(raw, out crowdCount) || crowdCount < 1) return false;

        return true;
    }

    public static bool TryExtractAgeImprovementTop10Change(
        string raw,
        out string clock,
        out int place,
        out int? previousPlace,
        out double improvement,
        out double ageReduction)
    {
        clock = string.Empty;
        place = 0;
        previousPlace = null;
        improvement = 0;
        ageReduction = 0;

        if (!TryExtractClock(raw, out var rawClock)) return false;
        var normalizedClock = NormalizeClock(rawClock);
        if (normalizedClock is null) return false;
        if (!TryExtractPlace(raw, out place) || place is < 1 or > 10) return false;

        if (TryExtractPreviousPlace(raw, out var parsedPreviousPlace))
        {
            if (parsedPreviousPlace is < 1 or > 10) return false;
            previousPlace = parsedPreviousPlace;
        }

        if (!TryExtractImprovement(raw, out improvement) || !double.IsFinite(improvement)) return false;
        if (!TryExtractAgeReduction(raw, out ageReduction) || !double.IsFinite(ageReduction)) return false;

        clock = normalizedClock;
        return true;
    }

    public static string NormalizeBadgeLabel(string? label)
    {
        var s = (label ?? string.Empty)
            .Replace("â€“", "-", StringComparison.Ordinal)
            .Replace('–', '-')
            .Replace('—', '-')
            .Trim();

        return s switch
        {
            "Age Reduction" => "Age reduction",
            "Chronological Age - Oldest" => "Chronological age – oldest",
            "Chronological age - oldest" => "Chronological age – oldest",
            "Chronological Age - Youngest" => "Chronological age – youngest",
            "Chronological age - youngest" => "Chronological age – youngest",
            "PhenoAge - Lowest" => "Pheno Age – lowest",
            "Pheno Age - lowest" => "Pheno Age – lowest",
            "PhenoAge Best Improvement" => "Pheno Age best improvement",
            "Bortz Age - Lowest" => "Bortz Age – lowest",
            "Bortz Age - lowest" => "Bortz Age – lowest",
            "Bortz Age Best Improvement" => "Bortz Age best improvement",
            "Pheno Pace of Aging" => "Pheno pace of aging",
            "Bortz Pace of Aging" => "Bortz pace of aging",
            "Most Submissions" => "Most submissions",
            ">=2 Submissions" => "≥2 submissions",
            "≥2 Submissions" => "≥2 submissions",
            "Crowd - Most Guessed" => "Crowd – most guessed",
            "Crowd - most guessed" => "Crowd – most guessed",
            "Crowd - Age Gap (Chrono−Crowd)" => "Crowd – age gap (chrono−crowd)",
            "Crowd - age gap (chrono−crowd)" => "Crowd – age gap (chrono−crowd)",
            "Crowd - Lowest Crowd Age" => "Crowd Age – lowest",
            "Crowd - lowest crowd age" => "Crowd Age – lowest",
            "Crowd Age - lowest" => "Crowd Age – lowest",
            "First Applicants" => "First applicants",
            "Perfect Application" => "Perfect application",
            _ when s.StartsWith("Best Domain - ", StringComparison.Ordinal) =>
                "Best domain – " + (s["Best Domain - ".Length..].Trim() switch
                {
                    "Vitamin D" => "vitamin D",
                    var domain => domain.ToLowerInvariant()
                }),
            _ when s.StartsWith("Best domain - ", StringComparison.Ordinal) =>
                "Best domain – " + s["Best domain - ".Length..].Trim(),
            _ => s.Replace(" - ", " – ", StringComparison.Ordinal)
        };
    }

    public static string ExtractDomainFromLabel(string? label)
    {
        var s = NormalizeBadgeLabel(label);
        if (string.IsNullOrEmpty(s)) return "Domain";
        var i = s.IndexOf('–');
        if (i < 0) i = s.IndexOf('-', StringComparison.Ordinal);
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

    static bool TryExtractDoubleField(string raw, string field, out double value)
    {
        value = 0;
        if (!TryExtractField(raw, field, out var s)) return false;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    static string? NormalizeClock(string? clock)
    {
        if (string.Equals(clock, "pheno", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(clock, "phenoage", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(clock, "pheno age", StringComparison.OrdinalIgnoreCase))
            return "pheno";

        if (string.Equals(clock, "bortz", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(clock, "bortzage", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(clock, "bortz age", StringComparison.OrdinalIgnoreCase))
            return "bortz";

        return null;
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
