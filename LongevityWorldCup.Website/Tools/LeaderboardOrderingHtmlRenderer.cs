using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class LeaderboardOrderingHtmlRenderer
{
    public static string Apply(string html, PublicLeaderboardSelection selection, DateTime asOf)
    {
        var state = selection.Ordering;
        var display = selection.DisplayOrdering;
        html = ReplaceSection(html, "ORDER-PRIMARY", Options(state.Sort, state.Then, false, selection.MetricLabel));
        html = ReplaceSection(html, "ORDER-SECONDARY", Options(state.Then, state.Sort, true, selection.MetricLabel));
        html = html.Replace("<!--ORDER-CATALOG-->", JsonSerializer.Serialize(new
        {
            asOf = asOf.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            fields = LeaderboardOrdering.Fields
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), StringComparison.Ordinal);
        html = ReplaceSection(html, "ORDER-RANK-HEADER", Header("rank", "Rank", display));
        html = ReplaceSection(html, "ORDER-NAME-HEADER", Header("name", "Athlete", display));
        html = ReplaceSection(html, "ORDER-METRIC-HEADER", Header("metric", selection.MetricLabel, display));
        html = html.Replace("<!--ORDER-EXTRA-HEADERS-->", string.Concat(display.Columns.Select(field => Header(field.Key, field.Label, display, field.Unit))), StringComparison.Ordinal);
        if (display.IsCustom)
        {
            html = html.Replace("class=\"leaderboard\"", "class=\"leaderboard leaderboard--ordered\"", StringComparison.Ordinal)
                .Replace("class=\"leaderboard-order-toggle\"", "class=\"leaderboard-order-toggle is-active\"", StringComparison.Ordinal);
        }
        if (selection.ByFlag) html = html.Replace("class=\"leaderboard-order-tools\"", "class=\"leaderboard-order-tools\" hidden", StringComparison.Ordinal);
        return html;
    }

    public static string Header(string key, string label, LeaderboardOrderState state, string unit = "")
    {
        var level = state.IsCustom && state.Sort == key ? 1 : state.Then == key ? 2 : 0;
        var direction = level == 2 ? state.ThenDirection : state.Direction;
        var action = key == "rank" ? "Restore official ranking" : level > 0
            ? $"{label}, {(level == 1 ? "order by" : "then by")}, {(direction == "asc" ? "ascending" : "descending")}; reverse order"
            : $"Order by {label}";
        var attributes = level > 0 ? $" data-order-level=\"{level}\" data-order-direction=\"{direction}\"" : "";
        if (level == 1) attributes += $" aria-sort=\"{(direction == "asc" ? "ascending" : "descending")}\"";
        var extra = key is not "rank" and not "name" and not "metric" ? " class=\"leaderboard-order-column\"" : "";
        var content = key == "metric" ? $"<span id=\"leaderboardMetricHeader\">{E(label)}</span>" : $"<span>{E(label)}</span>";
        if (unit.Length > 0) content += $"<small>{E(unit)}</small>";
        content += $"<span class=\"order-indicator\" aria-hidden=\"true\">{(level == 0 ? "" : direction == "asc" ? "↑" : "↓")}</span>";
        return $"<th scope=\"col\" data-order-key=\"{key}\"{extra}{attributes}><button type=\"button\" class=\"leaderboard-column-order\" aria-label=\"{E(action)}\">{content}</button>" +
            (key == "metric" ? "<a href=\"/ruleset#point-system-ranking\" class=\"leaderboard-metric-link leaderboard-rules-link\" aria-label=\"How competition rankings work\" title=\"How competition rankings work\"><i class=\"fa-regular fa-circle-question\" aria-hidden=\"true\"></i></a>" : "") + "</th>";
    }

    public static string Cell(LeaderboardSnapshotRow row, LeaderboardOrderField field)
    {
        var value = LeaderboardOrdering.Value(row, field.Key);
        var description = $"{field.Label}: {LeaderboardOrdering.Format(value, field)} {field.Unit}. Tested {value.TestDate}";
        var title = value.TestDate is null ? "" : $" title=\"{E(description)}\" aria-label=\"{E(description)}\"";
        var content = value.IsMissing ? "<span class=\"order-missing\" role=\"img\" aria-label=\"Not available\">—</span>" : E(LeaderboardOrdering.Format(value, field));
        return $"<td class=\"leaderboard-order-value\" data-order-key=\"{field.Key}\" data-label=\"{E(field.Label + (field.Unit.Length > 0 ? " (" + field.Unit + ")" : ""))}\"{title}>{content}</td>";
    }

    private static string Options(string? selected, string? other, bool secondary, string metricLabel)
    {
        var sb = new StringBuilder(secondary ? $"<option value=\"\"{(selected is null ? " selected" : "")}>None</option>" : "");
        foreach (var group in LeaderboardOrdering.Fields.Where(field => !secondary || field.Key != "rank").GroupBy(field => field.Group))
        {
            sb.Append($"<optgroup label=\"{E(group.Key)}\">");
            foreach (var field in group)
            {
                var label = field.Key == "metric" ? metricLabel : field.Label;
                if (field.Unit == "%") label += " (%)";
                sb.Append($"<option value=\"{field.Key}\"{(selected == field.Key ? " selected" : "")}{(other == field.Key ? " disabled hidden" : "")}>{E(label)}</option>");
            }
            sb.Append("</optgroup>");
        }
        return sb.ToString();
    }

    private static string ReplaceSection(string html, string marker, string content) => Regex.Replace(html,
        $"<!--{marker}-START-->.*?<!--{marker}-END-->", _ => $"<!--{marker}-START-->{content}<!--{marker}-END-->", RegexOptions.Singleline);
    private static string E(string value) => WebUtility.HtmlEncode(value);
}
