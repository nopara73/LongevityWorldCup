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
        var proCount = rows.Count(row => IsTrack(row, "Pro"));
        var amateurCount = rows.Count(row => IsTrack(row, "Amateur"));

        var sb = new StringBuilder();
        sb.AppendLine("<section class=\"homepage-first-viewport\" aria-labelledby=\"homepage-first-viewport-title\">");
        sb.AppendLine("    <div class=\"homepage-first-copy\">");
        sb.AppendLine("        <p class=\"homepage-first-kicker\">Ultimate League</p>");
        sb.AppendLine("        <h2 id=\"homepage-first-viewport-title\" class=\"homepage-first-title\">Reverse biological age. Rise on the leaderboard.</h2>");
        sb.AppendLine("        <p class=\"homepage-first-intro\">Proof-backed pheno age and Bortz Age results rank longevity athletes in public.</p>");
        sb.AppendLine("        <a class=\"homepage-first-link\" href=\"/leaderboard\">View leaderboard</a>");
        sb.AppendLine("        <div class=\"homepage-competition-line\" aria-label=\"Current Ultimate League status\">");
        AppendMetric(sb, rows.Count, "athletes");
        AppendMetric(sb, proCount, "Pro");
        AppendMetric(sb, amateurCount, "Amateur");
        sb.AppendLine("            <span>Pro before Amateur</span>");
        if (ultimateLeader is not null)
        {
            sb.Append("            <a href=\"")
                .Append(Encode(ultimateLeader.AthletePath))
                .Append("\">Ultimate #1: ")
                .Append(Encode(ultimateLeader.DisplayName))
                .AppendLine("</a>");
        }

        sb.AppendLine("        </div>");
        sb.AppendLine("    </div>");

        sb.AppendLine("</section>");

        return sb.ToString();
    }

    private static void AppendMetric(StringBuilder sb, int value, string label)
    {
        sb.Append("            <span><strong>")
            .Append(value.ToString(CultureInfo.InvariantCulture))
            .Append("</strong> ")
            .Append(Encode(label))
            .AppendLine("</span>");
    }

    private static bool IsTrack(LeaderboardSnapshotRow row, string track)
    {
        return string.Equals(row.Track, track, StringComparison.OrdinalIgnoreCase);
    }

    private static string Encode(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
