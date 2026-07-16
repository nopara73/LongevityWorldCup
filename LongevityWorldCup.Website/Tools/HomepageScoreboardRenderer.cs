using System.Globalization;
using System.Net;
using System.Text;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class HomepageScoreboardRenderer
{
    public static string Render(LeaderboardSnapshot snapshot)
    {
        if (snapshot.Rows.Count == 0)
        {
            return RenderUnavailable();
        }

        var leader = snapshot.Rows[0];
        var proCount = snapshot.Rows.Count(row => IsTrack(row, "Pro"));
        var amateurCount = snapshot.Rows.Count(row => IsTrack(row, "Amateur"));
        var score = FormatScore(leader.EffectiveAgeReductionYears);

        var html = new StringBuilder();
        AppendOpening(html, "live");
        AppendIntroduction(html, isLive: true);
        html.AppendLine("        <div class=\"homepage-scoreboard-record\" aria-label=\"Current Ultimate League leader\">");
        html.AppendLine("            <span class=\"homepage-scoreboard-record-label\">Current Ultimate League #1</span>");
        html.Append("            <a class=\"homepage-scoreboard-leader\" href=\"")
            .Append(Encode(leader.AthletePath))
            .Append("\">")
            .Append(Encode(leader.DisplayName))
            .AppendLine("</a>");
        html.Append("            <strong class=\"homepage-scoreboard-score")
            .Append(score is null ? " is-pending" : string.Empty)
            .AppendLine("\">");
        if (score is null)
        {
            html.AppendLine("                <span class=\"homepage-scoreboard-score-value\">Score pending</span>");
        }
        else
        {
            html.Append("                <span class=\"homepage-scoreboard-score-value\">")
                .Append(Encode(score.Value.Value))
                .AppendLine("</span>");
            html.Append("                <span class=\"homepage-scoreboard-score-unit\">")
                .Append(Encode(score.Value.Unit))
                .AppendLine("</span>");
        }

        html.AppendLine("            </strong>");
        html.AppendLine("            <span class=\"homepage-scoreboard-score-label\">Effective age reduction</span>");
        html.AppendLine("            <div class=\"homepage-scoreboard-metrics\" aria-label=\"Current ranked field\">");
        AppendMetric(html, snapshot.Rows.Count, "ranked athletes");
        AppendMetric(html, proCount, "Pro");
        AppendMetric(html, amateurCount, "Amateur");
        html.AppendLine("            </div>");
        html.AppendLine("        </div>");
        AppendClosing(html);
        return html.ToString();
    }

    public static string RenderUnavailable()
    {
        var html = new StringBuilder();
        AppendOpening(html, "unavailable");
        AppendIntroduction(html, isLive: false);
        html.AppendLine("        <div class=\"homepage-scoreboard-record is-unavailable\" role=\"status\">");
        html.AppendLine("            <span class=\"homepage-scoreboard-record-label\">Live standings</span>");
        html.AppendLine("            <strong class=\"homepage-scoreboard-unavailable-title\">Standings are updating</strong>");
        html.AppendLine("            <span class=\"homepage-scoreboard-unavailable-copy\">The live summary is temporarily unavailable. The official standings remain available below.</span>");
        html.AppendLine("            <a class=\"homepage-scoreboard-unavailable-link\" href=\"#live-standings\">Open standings</a>");
        html.AppendLine("        </div>");
        AppendClosing(html);
        return html.ToString();
    }

    private static void AppendOpening(StringBuilder html, string state)
    {
        html.Append("<section class=\"homepage-scoreboard\" data-state=\"")
            .Append(state)
            .AppendLine("\" aria-labelledby=\"homepage-scoreboard-title\">");
        html.AppendLine("    <div class=\"homepage-scoreboard-grid\">");
    }

    private static void AppendIntroduction(StringBuilder html, bool isLive)
    {
        html.AppendLine("        <div class=\"homepage-scoreboard-intro\">");
        html.Append("            <p class=\"homepage-scoreboard-kicker\"><span aria-hidden=\"true\"></span> Ultimate League")
            .Append(isLive ? " · Live" : string.Empty)
            .AppendLine("</p>");
        html.AppendLine("            <h1 id=\"homepage-scoreboard-title\" class=\"homepage-scoreboard-title\">The world championship of age reversal.</h1>");
        html.AppendLine("            <p class=\"homepage-scoreboard-lede\">Proof-backed pheno age and bortz age results rank longevity athletes in public. Pro athletes rank before Amateur athletes.</p>");
        html.AppendLine("            <div class=\"homepage-scoreboard-actions\">");
        html.AppendLine("                <a class=\"homepage-scoreboard-action\" href=\"#live-standings\">Explore live standings <span aria-hidden=\"true\">↓</span></a>");
        html.AppendLine("                <a class=\"homepage-scoreboard-rules\" href=\"/ruleset#point-system-ranking\">Ranking rules <span aria-hidden=\"true\">↗</span></a>");
        html.AppendLine("            </div>");
        html.AppendLine("        </div>");
    }

    private static void AppendMetric(StringBuilder html, int value, string label)
    {
        html.AppendLine("                <span class=\"homepage-scoreboard-metric\">");
        html.Append("                    <strong>")
            .Append(value.ToString(CultureInfo.InvariantCulture))
            .AppendLine("</strong>");
        html.Append("                    <span>")
            .Append(Encode(label))
            .AppendLine("</span>");
        html.AppendLine("                </span>");
    }

    private static void AppendClosing(StringBuilder html)
    {
        html.AppendLine("    </div>");
        html.AppendLine("    <div class=\"homepage-scoreboard-proofline\" aria-label=\"Competition basis\">");
        html.AppendLine("        <span><i class=\"fas fa-vial\" aria-hidden=\"true\"></i> Biological-age results</span>");
        html.AppendLine("        <span><i class=\"fas fa-file-shield\" aria-hidden=\"true\"></i> Public proof</span>");
        html.AppendLine("        <span><i class=\"fas fa-ranking-star\" aria-hidden=\"true\"></i> Pro before Amateur</span>");
        html.AppendLine("    </div>");
        html.AppendLine("</section>");
    }

    private static (string Value, string Unit)? FormatScore(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return null;
        }

        var magnitude = Math.Abs(value.Value).ToString("0.0", CultureInfo.InvariantCulture);
        if (value.Value < 0)
        {
            return (magnitude, "years younger");
        }

        if (value.Value > 0)
        {
            return (magnitude, "years older");
        }

        return ("0.0", "years difference");
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
