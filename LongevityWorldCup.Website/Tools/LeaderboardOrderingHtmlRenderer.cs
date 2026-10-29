using System.Net;
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
        html = ReplaceSection(html, "ORDER-PRIMARY", SelectedOption(state.Sort, selection.MetricLabel));
        html = ReplaceSection(html, "ORDER-SECONDARY", SelectedOption(state.Then, selection.MetricLabel));
        html = html.Replace("<!--ORDER-CATALOG-->", JsonSerializer.Serialize(new
        {
            asOf = asOf.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            // Group once and omit trailing defaults to keep the shared homepage catalog small.
            groups = LeaderboardOrdering.Fields.GroupBy(field => field.Group).Select(group => new
            {
                label = group.Key,
                fields = group.Select(CatalogEntry)
            })
        }), StringComparison.Ordinal);
        html = ReplaceSection(html, "ORDER-RANK-HEADER", Header("rank", "Rank", display));
        html = ReplaceSection(html, "ORDER-NAME-HEADER", Header("name", "Athlete", display));
        html = ReplaceSection(html, "ORDER-METRIC-HEADER", Header("metric", selection.MetricLabel, display));
        html = html.Replace("<!--ORDER-EXTRA-HEADERS-->", string.Concat(display.Columns.Select(field => Header(field.Key, field.Label, display, field.Unit))), StringComparison.Ordinal);
        if (display.IsCustom)
        {
            html = html.Replace("class=\"leaderboard\"", $"class=\"leaderboard leaderboard--ordered\" data-order-columns=\"{display.Columns.Count()}\"", StringComparison.Ordinal)
                .Replace("class=\"leaderboard-order-toggle\"", "class=\"leaderboard-order-toggle is-active\"", StringComparison.Ordinal)
                .Replace("id=\"leaderboardOrderReset\" hidden disabled", "id=\"leaderboardOrderReset\" disabled", StringComparison.Ordinal);
        }
        if (selection.ByFlag) html = html.Replace("class=\"leaderboard-order-tools\"", "class=\"leaderboard-order-tools\" hidden", StringComparison.Ordinal);
        return html;
    }

    // Wire row: key, label, unit, source key, decimals, direction, kind.
    // Null and omitted values use the same defaults as the browser decoder.
    private static object?[] CatalogEntry(LeaderboardOrderField field)
    {
        object?[] values =
        [
            field.Key, field.Label, field.Unit.Length == 0 ? null : field.Unit, field.SourceKey,
            field.Decimals == 1 ? null : field.Decimals,
            field.Direction == "asc" ? null : field.Direction,
            field.Kind == "number" ? null : field.Kind
        ];
        return values[..(Array.FindLastIndex(values, value => value is not null) + 1)];
    }

    public static string Header(string key, string label, LeaderboardOrderState state, string unit = "")
    {
        var level = state.IsCustom && state.Sort == key ? 1 : state.Then == key ? 2 : 0;
        var direction = level == 2 ? state.ThenDirection : state.Direction;
        var accessibleUnit = LeaderboardOrdering.Find(key)?.Unit ?? unit;
        var accessibleLabel = label + (accessibleUnit.Length > 0 ? $" ({accessibleUnit})" : "");
        var action = key == "rank" ? "Restore official ranking" : level > 0
            ? $"{accessibleLabel}, {(level == 1 ? "order by" : "then by")}, {(direction == "asc" ? "ascending" : "descending")}; reverse order"
            : $"Order by {accessibleLabel}";
        var attributes = level > 0 ? $" data-order-level=\"{level}\" data-order-direction=\"{direction}\"" : "";
        if (level == 1) attributes += $" aria-sort=\"{(direction == "asc" ? "ascending" : "descending")}\"";
        var extra = key is not "rank" and not "name" and not "metric" ? " class=\"leaderboard-order-column\"" : "";
        var content = key == "metric" ? $"<span id=\"leaderboardMetricHeader\" class=\"order-heading-label\">{E(label)}</span>" : $"<span class=\"order-heading-label\">{E(label)}</span>";
        if (unit.Length > 0) content += $"<small>{E(unit)}</small>";
        const string arrow = "<svg viewBox=\"0 0 20 20\"><path d=\"M10 4v12m-4-4 4 4 4-4\" /></svg>";
        content = $"<span class=\"order-heading\">{content}</span><span class=\"order-indicator\" aria-hidden=\"true\">{(level == 0 ? "" : arrow)}</span>";
        var button = $"<button type=\"button\" class=\"leaderboard-column-order\" aria-label=\"{E(action)}\">{content}</button>";
        if (key == "metric") button = "<span class=\"leaderboard-metric-heading\">" + button +
            "<a href=\"/ruleset#point-system-ranking\" class=\"leaderboard-metric-link leaderboard-rules-link\" aria-label=\"How competition rankings work\" title=\"How competition rankings work\"><i class=\"fa-regular fa-circle-question\" aria-hidden=\"true\"></i></a></span>";
        return $"<th scope=\"col\" data-order-key=\"{key}\"{extra}{attributes}>" + button + "</th>";
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
