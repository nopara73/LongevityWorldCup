using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Tools;
using Microsoft.Extensions.Caching.Memory;

namespace LongevityWorldCup.Website.Business;

public sealed class LeaderboardFactsService(AthleteDataService athletes, IMemoryCache cache)
{
    private const string CacheKey = "leaderboard-facts-markdown-v1";
    private const string AthleteNamesCacheKey = "athlete-names-markdown-v1";
    private const string SiteBaseUrl = "https://longevityworldcup.com";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly Regex EmailLike = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);

    private static readonly (string Slug, string DisplayName)[] LeagueSections =
    [
        ("ultimate", "Ultimate League"),
        ("amateur", "Amateur League"),
        ("mens", "Men's League"),
        ("womens", "Women's League"),
        ("open", "Open League"),
        ("silent-generation", "Silent Generation League"),
        ("baby-boomers", "Baby Boomers League"),
        ("gen-x", "Gen X League"),
        ("millennials", "Millennials League"),
        ("gen-z", "Gen Z League"),
        ("gen-alpha", "Gen Alpha League"),
        ("prosperan", "Prosperan League")
    ];

    public LeaderboardFactsDocument GetLeaderboardMarkdown()
    {
        return cache.GetOrCreate(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var markdown = RenderMarkdown(generatedAtUtc);
            return new LeaderboardFactsDocument(markdown, generatedAtUtc);
        })!;
    }

    public LeaderboardFactsDocument GetAthleteNamesMarkdown()
    {
        return cache.GetOrCreate(AthleteNamesCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var markdown = RenderAthleteNames();
            return new LeaderboardFactsDocument(markdown, generatedAtUtc);
        })!;
    }

    private string RenderMarkdown(DateTimeOffset generatedAtUtc)
    {
        var rows = BuildRows();
        var rowsBySlug = rows.ToDictionary(row => row.Slug, StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();

        sb.AppendLine("---");
        sb.AppendLine("title: Longevity World Cup leaderboard facts");
        sb.AppendLine($"canonical: {SiteBaseUrl}/ai/leaderboard.md");
        sb.AppendLine($"generated_at_utc: {generatedAtUtc:O}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("# Longevity World Cup Leaderboard Facts");
        sb.AppendLine();
        sb.AppendLine("This machine-readable page summarizes public Longevity World Cup leaderboard facts for search and retrieval systems. For the interactive human page, use https://longevityworldcup.com/leaderboard.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- Athlete count: {rows.Count.ToString(CultureInfo.InvariantCulture)}");
        if (rows.FirstOrDefault() is { } leader)
        {
            sb.AppendLine($"- Current Ultimate League leader: {MarkdownLink(leader.DisplayName, leader.AthleteUrl)}");
        }
        if (rows.FirstOrDefault(row => row.Track == "Pro") is { } proLeader)
        {
            sb.AppendLine($"- Current Pro leader: {MarkdownLink(proLeader.DisplayName, proLeader.AthleteUrl)}");
        }
        if (rows.FirstOrDefault(row => row.Track == "Amateur") is { } amateurLeader)
        {
            sb.AppendLine($"- Current Amateur leader: {MarkdownLink(amateurLeader.DisplayName, amateurLeader.AthleteUrl)}");
        }
        sb.AppendLine();
        sb.AppendLine("## Ranking Rules");
        sb.AppendLine();
        sb.AppendLine("- Ultimate League ranking uses the backend competition order.");
        sb.AppendLine("- Pro athletes, identified by an eligible Bortz Age result, rank ahead of Amateur athletes in the Ultimate League.");
        sb.AppendLine("- Within the same track, more negative age reduction ranks higher.");
        sb.AppendLine("- If age reduction ties, older chronological age ranks higher, then athlete name breaks remaining ties alphabetically.");
        sb.AppendLine("- Bortz Age is the Pro seasonal clock; PhenoAge is the Amateur all-time clock.");
        sb.AppendLine();

        AppendRankingTable(sb, "Ultimate League Rankings", rows);
        AppendRankingTable(sb, "Top Pro Athletes", rows.Where(row => row.Track == "Pro").Take(25));
        AppendRankingTable(sb, "Top Amateur Athletes", rows.Where(row => row.Track == "Amateur").Take(25));
        AppendLeagueLeaders(sb, rowsBySlug);

        return sb.ToString();
    }

    private string RenderAthleteNames()
    {
        var sb = new StringBuilder();
        var number = 1;
        foreach (var row in BuildRows())
        {
            sb.AppendLine($"{number.ToString(CultureInfo.InvariantCulture)}. {SanitizeLine(row.DisplayName)}");
            number++;
        }

        return sb.ToString();
    }

    private IReadOnlyList<LeaderboardFactRow> BuildRows()
    {
        var ranked = athletes.GetRankingsOrder();
        var snapshot = athletes.GetAthletesSnapshot();
        var extraBySlug = snapshot
            .OfType<JsonObject>()
            .Select(BuildExtra)
            .Where(extra => !string.IsNullOrWhiteSpace(extra.Slug))
            .ToDictionary(extra => extra.Slug, StringComparer.OrdinalIgnoreCase);

        var rows = new List<LeaderboardFactRow>();
        var rank = 0;
        foreach (var node in ranked.OfType<JsonObject>())
        {
            var slug = GetString(node, "AthleteSlug");
            if (string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            rank++;
            extraBySlug.TryGetValue(slug, out var extra);
            extra ??= AthleteExtra.Empty;
            var name = FirstNonEmpty(extra.DisplayName, GetString(node, "Name"), slug);
            var hasBortz = TryGetDouble(node, "LowestBortzAge", out var lowestBortzAge);
            TryGetDouble(node, "LowestPhenoAge", out var lowestPhenoAge);
            TryGetDouble(node, "ChronologicalAge", out var chronologicalAge);
            TryGetDouble(node, "AgeDifference", out var ageDifference);

            rows.Add(new LeaderboardFactRow(
                Rank: rank,
                Slug: slug,
                DisplayName: name,
                AthleteUrl: $"{SiteBaseUrl}/athlete/{slug.Replace('_', '-')}",
                Track: hasBortz ? "Pro" : "Amateur",
                EffectiveAgeReductionYears: ageDifference,
                LowestBortzAge: hasBortz ? lowestBortzAge : null,
                LowestPhenoAge: lowestPhenoAge,
                ChronologicalAge: chronologicalAge,
                Division: extra.Division,
                Generation: extra.Generation,
                Flag: extra.Flag,
                ExclusiveLeague: extra.ExclusiveLeague,
                MediaContact: FilterMediaContact(extra.MediaContact)));
        }

        return rows;
    }

    private void AppendLeagueLeaders(StringBuilder sb, IReadOnlyDictionary<string, LeaderboardFactRow> rowsBySlug)
    {
        sb.AppendLine("## League Leaders");
        sb.AppendLine();
        sb.AppendLine("| League | Rank in league | Ultimate rank | Athlete | Track | Age reduction |");
        sb.AppendLine("| --- | ---: | ---: | --- | --- | ---: |");

        foreach (var (slug, displayName) in LeagueSections)
        {
            var topSlugs = athletes.GetTop3SlugsForLeague(slug);
            for (var i = 0; i < topSlugs.Count; i++)
            {
                if (!rowsBySlug.TryGetValue(topSlugs[i], out var row))
                {
                    continue;
                }

                sb.AppendLine(
                    $"| {EscapeTable(displayName)} | {(i + 1).ToString(CultureInfo.InvariantCulture)} | {row.Rank.ToString(CultureInfo.InvariantCulture)} | {MarkdownLink(row.DisplayName, row.AthleteUrl)} | {EscapeTable(row.Track)} | {FormatYears(row.EffectiveAgeReductionYears)} |");
            }
        }

        sb.AppendLine();
    }

    private static void AppendRankingTable(StringBuilder sb, string title, IEnumerable<LeaderboardFactRow> rows)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        sb.AppendLine("| Rank | Athlete | Track | Age reduction | Lowest Bortz Age | Lowest PhenoAge | Chronological age | Division | Generation | Flag | Exclusive league | Media contact |");
        sb.AppendLine("| ---: | --- | --- | ---: | ---: | ---: | ---: | --- | --- | --- | --- | --- |");

        foreach (var row in rows)
        {
            sb.AppendLine(
                $"| {row.Rank.ToString(CultureInfo.InvariantCulture)} | {MarkdownLink(row.DisplayName, row.AthleteUrl)} | {EscapeTable(row.Track)} | {FormatYears(row.EffectiveAgeReductionYears)} | {FormatNumber(row.LowestBortzAge)} | {FormatNumber(row.LowestPhenoAge)} | {FormatNumber(row.ChronologicalAge)} | {EscapeTable(row.Division)} | {EscapeTable(row.Generation)} | {EscapeTable(row.Flag)} | {EscapeTable(row.ExclusiveLeague)} | {EscapeTable(row.MediaContact)} |");
        }

        sb.AppendLine();
    }

    private static AthleteExtra BuildExtra(JsonObject athlete)
    {
        var slug = GetString(athlete, "AthleteSlug");
        return new AthleteExtra(
            Slug: slug,
            DisplayName: FirstNonEmpty(GetString(athlete, "DisplayName"), GetString(athlete, "Name"), slug),
            Division: GetString(athlete, "Division"),
            Generation: GenerationResolver.ResolveFromAthleteJson(athlete) ?? "",
            Flag: GetString(athlete, "Flag"),
            ExclusiveLeague: GetString(athlete, "ExclusiveLeague"),
            MediaContact: GetString(athlete, "MediaContact"));
    }

    private static string FilterMediaContact(string mediaContact)
    {
        if (string.IsNullOrWhiteSpace(mediaContact))
        {
            return "";
        }

        var trimmed = mediaContact.Trim();
        return EmailLike.IsMatch(trimmed) ? "" : trimmed;
    }

    private static string MarkdownLink(string text, string url)
    {
        return $"[{EscapeLinkText(text)}]({url})";
    }

    private static string EscapeLinkText(string text)
    {
        return EscapeTable(text).Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
    }

    private static string EscapeTable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Trim();
    }

    private static string SanitizeLine(string value)
    {
        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static string FormatYears(double? value)
    {
        return value.HasValue ? $"{value.Value.ToString("+#0.0;-#0.0;0.0", CultureInfo.InvariantCulture)} years" : "";
    }

    private static string FormatNumber(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private static string GetString(JsonObject obj, string propertyName)
    {
        return obj[propertyName] is JsonValue value && value.TryGetValue<string>(out var result)
            ? result?.Trim() ?? ""
            : "";
    }

    private static bool TryGetDouble(JsonObject obj, string propertyName, out double value)
    {
        value = 0;
        return obj[propertyName] is JsonValue jsonValue &&
               jsonValue.TryGetValue<double>(out value) &&
               !double.IsNaN(value) &&
               !double.IsInfinity(value);
    }

    private sealed record AthleteExtra(
        string Slug,
        string DisplayName,
        string Division,
        string Generation,
        string Flag,
        string ExclusiveLeague,
        string MediaContact)
    {
        public static readonly AthleteExtra Empty = new("", "", "", "", "", "", "");
    }

    private sealed record LeaderboardFactRow(
        int Rank,
        string Slug,
        string DisplayName,
        string AthleteUrl,
        string Track,
        double? EffectiveAgeReductionYears,
        double? LowestBortzAge,
        double? LowestPhenoAge,
        double? ChronologicalAge,
        string Division,
        string Generation,
        string Flag,
        string ExclusiveLeague,
        string MediaContact);
}

public sealed record LeaderboardFactsDocument(string Markdown, DateTimeOffset LastModifiedUtc);
