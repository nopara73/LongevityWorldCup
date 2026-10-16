using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class LeaderboardOrderingHtmlRenderer
{
    public static string Apply(string html, PublicLeaderboardSelection selection, DateTime asOf)
    {
        var state = selection.Ordering;
        var display = selection.DisplayOrdering;
        html = ReplaceSection(html, "ORDER-PRIMARY", SelectedOption(state.Sort, selection.MetricLabel));
        html = ReplaceSection(html, "ORDER-SECONDARY", SelectedOption(state.Then, selection.MetricLabel));
        html = html.Replace("<!--ORDER-CATALOG-->", JsonSerializer.Serialize(new
        {
            asOf = asOf.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            // The browser expands defaults and builds both pickers from this one catalog.
            fields = LeaderboardOrdering.Fields.Select(field => new
            {
                field.Key, field.Label, field.Group,
                Unit = field.Unit.Length == 0 ? null : field.Unit,
                Decimals = field.Decimals == 1 ? (int?)null : field.Decimals,
                Direction = field.Direction == "asc" ? null : field.Direction,
                Kind = field.Kind == "number" ? null : field.Kind
            })
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }), StringComparison.Ordinal);
        html = ReplaceSection(html, "ORDER-RANK-HEADER", Header("rank", "Rank", display));
        html = ReplaceSection(html, "ORDER-NAME-HEADER", Header("name", "Athlete", display));
        html = ReplaceSection(html, "ORDER-METRIC-HEADER", Header("metric", selection.MetricLabel, display));
        html = html.Replace("<!--ORDER-EXTRA-HEADERS-->", string.Concat(display.Columns.Select(field => Header(field.Key, field.Label, display, field.Unit))), StringComparison.Ordinal);
        if (display.IsCustom)
        {
            html = html.Replace("class=\"leaderboard\"", $"class=\"leaderboard leaderboard--ordered\" data-order-columns=\"{display.Columns.Count()}\"", StringComparison.Ordinal)
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
        var content = key == "metric" ? $"<span id=\"leaderboardMetricHeader\" class=\"order-heading-label\">{E(label)}</span>" : $"<span class=\"order-heading-label\">{E(label)}</span>";
        if (unit.Length > 0) content += $"<small>{E(unit)}</small>";
        content = $"<span class=\"order-heading\">{content}</span><span class=\"order-indicator\" aria-hidden=\"true\">{(level == 0 ? "" : direction == "asc" ? "↑" : "↓")}</span>";
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

    // Controls stay disabled until hydration; the first paint only needs the selected label.
    private static string SelectedOption(string? selected, string metricLabel)
    {
        var field = LeaderboardOrdering.Find(selected);
        var label = field is null ? "None" : field.Key == "metric" ? metricLabel : field.Label + (field.Unit == "%" ? " (%)" : "");
        return $"<option value=\"{E(selected ?? "")}\" selected>{E(label)}</option>";
    }

    private static string ReplaceSection(string html, string marker, string content) => Regex.Replace(html,
        $"<!--{marker}-START-->.*?<!--{marker}-END-->", _ => $"<!--{marker}-START-->{content}<!--{marker}-END-->", RegexOptions.Singleline);
    private static string E(string value) => WebUtility.HtmlEncode(value);
}
