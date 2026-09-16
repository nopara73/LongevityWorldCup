using System.Globalization;
using System.Net;
using System.Text;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class LeaderboardHtmlRenderer
{
    public const string ServerRenderedTbodyAttributes = "data-server-rendered=\"true\" aria-busy=\"false\"";

    public static string RenderPodium(LeaderboardSnapshot snapshot, IReadOnlySet<string> visibleSlugs)
    {
        var sb = new StringBuilder();
        foreach (var row in snapshot.Rows.Take(3))
        {
            var rankClass = new[] { "first", "second", "third" }[row.Rank - 1];
            var icon = new[] { "crown", "medal", "award" }[row.Rank - 1];
            var color = new[] { "gold", "silver", "#cd7f32" }[row.Rank - 1];
            var place = new[] { "1st", "2nd", "3rd" }[row.Rank - 1];
            var blurred = visibleSlugs.Contains(row.Slug) ? "" : " blurred";
            sb.Append($"<div class=\"podium-item {rankClass}{blurred}\" data-athlete-name=\"{EncodeAttribute(row.AthleteName ?? row.DisplayName)}\">")
                .Append($"<div class=\"podium-rank\" title=\"{place} Place\"><i class=\"fa-solid fa-{icon}\" style=\"color: {color};\"></i></div>")
                .Append($"<img src=\"{EncodeAttribute(row.LeaderboardThumbnailUrl)}\" alt=\"{EncodeAttribute(row.DisplayName)} portrait\" class=\"podium-portrait\" loading=\"lazy\">")
                .Append($"<div class=\"name-row\"><a class=\"athlete-name\" href=\"{EncodeAttribute(row.AthletePath)}\" title=\"View stats of {EncodeAttribute(row.DisplayName)}\">{EncodeText(row.DisplayName)}</a></div>")
                .Append("<div class=\"podium-link-row\"></div>")
                .Append($"<div><span class=\"age-reduction\">{PublicHtmlFormat.Fixed(Math.Abs(row.EffectiveAgeReductionYears ?? 0), row.MetricDecimals)} years</span> reduced</div>")
                .Append("<a class=\"podium-item-lower\" href=\"#contribute\" aria-label=\"Donate to the prize pool\" aria-busy=\"true\"><div class=\"btc-amount\">&nbsp;</div><div class=\"prize-money\">&mdash;</div></a></div>");
        }
        return sb.ToString();
    }

    public static string RenderRows(LeaderboardSnapshot snapshot, string metricLabel = "Age reduction", bool separateTracks = true)
    {
        if (snapshot.Rows.Count == 0)
        {
            return "<tr class=\"no-results\"><td colspan=\"6\"><div class=\"no-results-empty-state\"><p>No athletes match this leaderboard.</p><a href=\"/leaderboard\" class=\"show-all-athletes-btn\">Show all athletes</a></div></td></tr>";
        }

        var sb = new StringBuilder();
        var previousTier = "";
        foreach (var row in snapshot.Rows)
        {
            if (separateTracks && previousTier == "pro" && row.Tier == "amateur")
                sb.AppendLine("<tr class=\"tier-separator\" aria-label=\"Start of Amateur track (Pheno Age) section\"><td colspan=\"5\" class=\"tier-separator-cell\"><span class=\"tier-separator-label\">Amateur track |<a href=\"https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/pheno-age.pdf\" target=\"_blank\" rel=\"noopener\" class=\"tier-separator-link\">Pheno Age</a></span></td></tr>");
            AppendRow(sb, row, metricLabel);
            previousTier = row.Tier;
        }

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, LeaderboardSnapshotRow row, string metricLabel)
    {
        var tier = string.Equals(row.Tier, "pro", StringComparison.OrdinalIgnoreCase) ? "pro" : "amateur";
        var rank = row.Rank.ToString(CultureInfo.InvariantCulture);
        var athletePath = EncodeAttribute(row.AthletePath);
        var displayName = EncodeText(row.DisplayName);
        var ageReduction = EncodeText(PublicHtmlFormat.Signed(row.EffectiveAgeReductionYears, row.MetricDecimals) + " years");
        var thumbnail = IsGeneratedAssetUrl(row.LeaderboardThumbnailUrl)
            ? row.LeaderboardThumbnailUrl
            : null;

        sb.Append("                <tr id=\"rank-")
            .Append(row.AnchorRank ?? row.Rank)
            .Append("\" class=\"tier-")
            .Append(tier)
            .Append(" server-rendered-leaderboard-row\" data-tier=\"")
            .Append(tier)
            .Append("\" data-athlete-name=\"")
            .Append(EncodeAttribute(row.AthleteName ?? row.DisplayName))
            .AppendLine("\">");
        sb.Append("                    <td data-label=\"Rank\" class=\"rank-td\"><span class=\"rank\">")
            .Append(rank)
            .AppendLine("</span></td>");
        sb.AppendLine("                    <td data-label=\"Athlete\" class=\"athlete-td\">");
        if (!string.IsNullOrWhiteSpace(thumbnail))
        {
            sb.Append("                        <span class=\"portrait-wrapper\"><img src=\"")
                .Append(EncodeAttribute(thumbnail))
                .Append("\" alt=\"")
                .Append(EncodeAttribute($"{row.DisplayName} portrait"))
                .AppendLine("\" class=\"portrait\" loading=\"lazy\"></span>");
        }
        sb.Append("                        <a class=\"athlete-name\" href=\"")
            .Append(athletePath)
            .Append("\">")
            .Append(displayName)
            .AppendLine("</a>");
        sb.AppendLine("                    </td>");
        sb.AppendLine("                    <td data-label=\"Sponsor\" class=\"sponsor-td\"></td>");
        sb.Append("                    <td data-label=\"").Append(EncodeAttribute(metricLabel)).Append("\" class=\"age-reduction-td\"><span class=\"age-reduction\">")
            .Append(ageReduction)
            .AppendLine("</span></td>");
        sb.Append("                    <td data-label=\"Media contact\" class=\"media-contact-td\">")
            .Append(RenderMediaContact(row))
            .AppendLine("</td>");
        sb.AppendLine("                </tr>");
    }

    private static string RenderMediaContact(LeaderboardSnapshotRow row)
    {
        if (string.IsNullOrWhiteSpace(row.MediaContact))
        {
            return "";
        }

        var href = row.MediaContact.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   row.MediaContact.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? row.MediaContact
            : $"https://{row.MediaContact}";

        return $"<a href=\"{EncodeAttribute(href)}\" target=\"_blank\" rel=\"noopener\" class=\"media-contact\">Contact</a>";
    }

    private static bool IsGeneratedAssetUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               url.StartsWith("/generated/", StringComparison.OrdinalIgnoreCase);
    }

    private static string EncodeText(string value)
    {
        return WebUtility.HtmlEncode(value ?? "");
    }

    private static string EncodeAttribute(string? value)
    {
        return WebUtility.HtmlEncode(value ?? "");
    }
}
