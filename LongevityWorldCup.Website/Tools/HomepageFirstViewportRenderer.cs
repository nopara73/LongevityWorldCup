using System.Globalization;
using System.Net;
using System.Text;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class HomepageFirstViewportRenderer
{
    public static string Render(LeaderboardSnapshot snapshot)
    {
        var rows = snapshot.Rows;
        var ultimateLeader = rows.FirstOrDefault();
        var amateurLeader = rows.FirstOrDefault(row => IsTrack(row, "Amateur"));
        var proCount = rows.Count(row => IsTrack(row, "Pro"));
        var amateurCount = rows.Count(row => IsTrack(row, "Amateur"));

        var sb = new StringBuilder();
        sb.AppendLine("<section class=\"homepage-first-viewport\" aria-labelledby=\"homepage-first-viewport-title\">");
        sb.AppendLine("    <div class=\"homepage-hero-panel\">");
        sb.AppendLine("        <p class=\"homepage-hero-kicker\">Live longevity sport</p>");
        sb.AppendLine("        <h2 id=\"homepage-first-viewport-title\" class=\"homepage-hero-title\">Reverse biological age, rise on a public leaderboard.</h2>");
        sb.AppendLine("        <p class=\"homepage-hero-intro\">Proof-backed biological aging clock results rank longevity athletes in the Ultimate League.</p>");
        sb.AppendLine("        <div class=\"homepage-hero-actions\" aria-label=\"Primary actions\">");
        sb.AppendLine("            <a class=\"homepage-hero-action homepage-hero-action-primary\" href=\"/play\">Apply as athlete</a>");
        sb.AppendLine("            <a class=\"homepage-hero-action homepage-hero-action-secondary\" href=\"/leaderboard\">View leaderboard</a>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"homepage-signal-row\" aria-label=\"Competition signals\">");
        sb.AppendLine("            <span class=\"homepage-signal-chip\">Proof-backed results</span>");
        sb.AppendLine("            <span class=\"homepage-signal-chip\">Pheno Age + Bortz Age</span>");
        sb.AppendLine("            <span class=\"homepage-signal-chip\">Pro before Amateur</span>");
        sb.AppendLine("        </div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <aside class=\"homepage-live-panel\" aria-label=\"Current Ultimate League status\">");
        sb.AppendLine("        <div class=\"homepage-live-header\">");
        sb.AppendLine("            <span class=\"homepage-live-label\">Ultimate League</span>");
        sb.AppendLine("            <span class=\"homepage-live-rule\">Pro before Amateur, then Effective Age Reduction</span>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"homepage-live-metrics\" aria-label=\"Current field size\">");
        AppendMetric(sb, rows.Count, "athletes");
        AppendMetric(sb, proCount, "Pro");
        AppendMetric(sb, amateurCount, "Amateur");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"homepage-rank-list\">");
        if (ultimateLeader is not null)
        {
            AppendRankCard(sb, "Ultimate #1", ultimateLeader, isPrimary: true);
        }

        if (amateurLeader is not null && !string.Equals(amateurLeader.Slug, ultimateLeader?.Slug, StringComparison.OrdinalIgnoreCase))
        {
            AppendRankCard(sb, "Top Amateur", amateurLeader, isPrimary: false);
        }

        if (ultimateLeader is null)
        {
            sb.AppendLine("            <p class=\"homepage-live-empty\">Leaderboard data is loading.</p>");
        }

        sb.AppendLine("        </div>");
        sb.AppendLine("    </aside>");
        sb.AppendLine("</section>");

        return sb.ToString();
    }

    private static void AppendMetric(StringBuilder sb, int value, string label)
    {
        sb.Append("            <span><strong>")
            .Append(value.ToString(CultureInfo.InvariantCulture))
            .Append("</strong><span>")
            .Append(Encode(label))
            .AppendLine("</span></span>");
    }

    private static void AppendRankCard(StringBuilder sb, string label, LeaderboardSnapshotRow row, bool isPrimary)
    {
        var cardClass = isPrimary
            ? "homepage-rank-card homepage-rank-card-primary"
            : "homepage-rank-card";

        sb.Append("            <a class=\"")
            .Append(cardClass)
            .Append("\" href=\"")
            .Append(Encode(row.AthletePath))
            .Append("\" data-homepage-rank-card=\"true\">")
            .AppendLine();

        AppendRankPortrait(sb, row);

        sb.AppendLine("                <span class=\"homepage-rank-copy\">");
        sb.Append("                    <span class=\"homepage-rank-label\">")
            .Append(Encode(label))
            .Append(" &middot; ")
            .Append(Encode(row.Track))
            .AppendLine("</span>");
        sb.Append("                    <span class=\"homepage-rank-name\">")
            .Append(Encode(row.DisplayName))
            .AppendLine("</span>");
        sb.Append("                    <span class=\"homepage-rank-score\">Age Reduction <strong>")
            .Append(Encode(FormatSignedYears(row.EffectiveAgeReductionYears)))
            .AppendLine("</strong></span>");
        sb.AppendLine("                </span>");
        sb.AppendLine("            </a>");
    }

    private static void AppendRankPortrait(StringBuilder sb, LeaderboardSnapshotRow row)
    {
        if (IsGeneratedAssetUrl(row.LeaderboardThumbnailUrl))
        {
            sb.Append("                <img class=\"homepage-rank-portrait\" src=\"")
                .Append(Encode(row.LeaderboardThumbnailUrl!))
                .Append("\" alt=\"")
                .Append(Encode($"{row.DisplayName} portrait"))
                .AppendLine("\" loading=\"eager\" fetchpriority=\"high\" decoding=\"async\">");
            return;
        }

        sb.Append("                <span class=\"homepage-rank-initials\" aria-hidden=\"true\">")
            .Append(Encode(BuildInitials(row.DisplayName)))
            .AppendLine("</span>");
    }

    private static string BuildInitials(string displayName)
    {
        var parts = displayName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length > 0)
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0]).ToString())
            .ToArray();

        return parts.Length == 0 ? "LW" : string.Concat(parts);
    }

    private static bool IsTrack(LeaderboardSnapshotRow row, string track)
    {
        return string.Equals(row.Track, track, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedAssetUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               url.StartsWith("/generated/", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatSignedYears(double? value)
    {
        return value.HasValue
            ? $"{value.Value.ToString("+#0.0;-#0.0;0.0", CultureInfo.InvariantCulture)} years"
            : "pending";
    }

    private static string Encode(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
