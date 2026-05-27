using System.Globalization;
using System.Net;
using System.Text;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class LeaderboardHtmlRenderer
{
    public const string ServerRenderedTbodyAttributes = "data-server-rendered=\"true\" aria-busy=\"false\"";

    public static string RenderRows(LeaderboardSnapshot snapshot)
    {
        if (snapshot.Rows.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder();
        foreach (var row in snapshot.Rows)
        {
            AppendRow(sb, row);
        }

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, LeaderboardSnapshotRow row)
    {
        var tier = string.Equals(row.Tier, "pro", StringComparison.OrdinalIgnoreCase) ? "pro" : "amateur";
        var rank = row.Rank.ToString(CultureInfo.InvariantCulture);
        var athletePath = EncodeAttribute(row.AthletePath);
        var displayName = EncodeText(row.DisplayName);
        var ageReduction = EncodeText(FormatYears(row.EffectiveAgeReductionYears));
        var thumbnail = IsGeneratedAssetUrl(row.LeaderboardThumbnailUrl)
            ? row.LeaderboardThumbnailUrl
            : null;

        sb.Append("                <tr id=\"rank-")
            .Append(rank)
            .Append("\" class=\"tier-")
            .Append(tier)
            .Append(" server-rendered-leaderboard-row\" data-tier=\"")
            .Append(tier)
            .Append("\" data-athlete-name=\"")
            .Append(EncodeAttribute(row.DisplayName))
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
        sb.Append("                    <td data-label=\"Age reduction\" class=\"age-reduction-td\"><span class=\"age-reduction\">")
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

    private static string FormatYears(double? value)
    {
        return value.HasValue ? $"{value.Value.ToString("+#0.0;-#0.0;0.0", CultureInfo.InvariantCulture)} years" : "";
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
