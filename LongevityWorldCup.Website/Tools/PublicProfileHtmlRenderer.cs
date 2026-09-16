using System.Net;
using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

/// <summary>Fills the existing dialog; its client controller enhances these same elements.</summary>
public static class PublicProfileHtmlRenderer
{
    public static string Render(string html, PublicLeaderboardSnapshot snapshot, PublicAthlete athlete, DateTime today)
    {
        var row = athlete.Row;
        var stats = athlete.Stats;
        html = html.Replace("id=\"detailsModal\" class=\"modal\"", "id=\"detailsModal\" class=\"modal\" style=\"display: block;\"", StringComparison.Ordinal);
        html = html.Replace("<div class=\"modal-content\">", $"<div class=\"modal-content\" data-server-rendered-profile=\"{Encode(row.RouteSlug)}\" data-athlete-slug=\"{Encode(row.RouteSlug)}\">", StringComparison.Ordinal);
        html = SetContent(html, "athleteName", Encode(row.DisplayName));
        html = SetContent(html, "stickyAthleteName", Encode(row.DisplayName));
        html = SetContent(html, "athleteBio", Encode(athlete.Source["Why"]?.GetValue<string>() ?? ""));
        html = html.Replace("id=\"shareAthleteProfile\" type=\"button\"", "id=\"shareAthleteProfile\" type=\"button\" disabled", StringComparison.Ordinal);
        html = PopulateLink(html, "personalLink", athlete.Source["PersonalLink"]?.GetValue<string>());
        html = PopulateLink(html, "mediaContact", row.MediaContact);
        var profilePic = row.LeaderboardThumbnailUrl ?? "";
        html = Regex.Replace(html, "(<img id=\"modalProfilePic\"\\s+src=\")[^\"]*(\"\\s+alt=\")[^\"]*", match =>
            match.Groups[1].Value + Encode(profilePic) + match.Groups[2].Value + Encode(row.DisplayName + " Profile Picture"));
        if (athlete.IsPro)
        {
            html = html.Replace("class=\"athlete-profile\" id=\"athlete-profile\"", "class=\"athlete-profile is-pro\" id=\"athlete-profile\"", StringComparison.Ordinal);
            html = html.Replace("class=\"modal-sticky-header\"", "class=\"modal-sticky-header is-pro\"", StringComparison.Ordinal);
        }

        // Match the existing title rule: Ultimate rank in the title unless a
        // different ranking view is strictly better. Keep canonical row anchors.
        var viewRanks = new[] { "bortz", "pheno", "bortz-improvement", "improvement", "crowd" }
            .Select(view => (View: view, Rank: snapshot.Order(view).ToList().FindIndex(a => a.Row.Slug == row.Slug) + 1))
            .Where(candidate => candidate.Rank > 0 && candidate.Rank < row.Rank)
            .OrderBy(candidate => candidate.Rank).ToList();
        if (viewRanks.Count == 0)
            html = SetContent(html, "athleteName", Encode(row.DisplayName) + $"<span class=\"modal-rank-text\"> (#{row.Rank})</span>");
        else
        {
            var best = viewRanks[0];
            var labels = new Dictionary<string, string>
            {
                ["bortz"] = "Bortz Age", ["pheno"] = "Pheno Age", ["improvement"] = "Pheno Improvement",
                ["bortz-improvement"] = "Bortz Improvement", ["crowd"] = "Crowd Age"
            };
            html = SetContent(html, "athleteRankings",
                RankingLink("ultimate", "Ultimate League", row.Rank, $"/leaderboard#rank-{row.Rank}") +
                RankingLink(best.View == "improvement" ? "pheno-improvement" : best.View, labels[best.View], best.Rank, $"/league/{best.View}#rank-{row.Rank}"));
            html = html.Replace("id=\"athleteRankings\" aria-label=\"Competition rankings\" hidden", "id=\"athleteRankings\" aria-label=\"Competition rankings\"", StringComparison.Ordinal);
        }

        // The best-rank badge and personalized/chart sections are populated by
        // the existing client. Do not publish a provisional or guessed best rank.
        html = html.Replace("<tr>\r\n                                <th scope=\"row\"><strong>Best rank:", "<tr data-profile-pending hidden>\r\n                                <th scope=\"row\"><strong>Best rank:", StringComparison.Ordinal)
            .Replace("<tr>\n                                <th scope=\"row\"><strong>Best rank:", "<tr data-profile-pending hidden>\n                                <th scope=\"row\"><strong>Best rank:", StringComparison.Ordinal);
        html = SetContent(html, "athleteDivision", LeagueLink(row.Division));
        html = SetContent(html, "athleteGeneration", LeagueLink(row.Generation));
        html = SetContent(html, "athleteFlag", FlagRouteCatalog.TryCreate(row.Flag, out var flag)
            ? $"<a class=\"league-link\" href=\"{Encode(flag.Path)}\">{Encode(flag.Name)}</a>" : "");
        var completedAge = stats.DobUtc is DateTime dob ? today.Year - dob.Year - (today.Date < dob.AddYears(today.Year - dob.Year) ? 1 : 0) : 0;
        html = SetContent(html, "chronologicalAge", completedAge.ToString(System.Globalization.CultureInfo.InvariantCulture));
        html = SetContent(html, "lowestPhenoAge", PublicHtmlFormat.Fixed(stats.LowestPhenoAge));
        html = SetContent(html, "ageReduction", SignedProfile(stats.AgeReduction));
        html = SetContent(html, "ageReductionPercent", PublicHtmlFormat.Fixed((1 - stats.PhenoPaceOfAging) * 100));
        html = SetContent(html, "ageReductionAccelerationLabel", stats.AgeReduction < 0 ? "Pheno Age reduction:" : "Pheno Age acceleration:");
        var effectiveReduction = athlete.Metric("ultimate");
        foreach (var prefix in new[] { "ageRadarCenter", "ageRadarFallback" })
        {
            html = SetContent(html, prefix + "Value", PublicHtmlFormat.Signed(effectiveReduction) + " years");
            html = SetContent(html, prefix + "Label", effectiveReduction < 0 ? "age reduction" : "age acceleration");
            html = SetContent(html, prefix + "Bio", "biological age: " + PublicHtmlFormat.Fixed(stats.LowestBortzAge ?? stats.LowestPhenoAge));
        }
        if (athlete.IsPro)
        {
            html = ShowRow(html, "lowestBortzAgeContainer");
            html = ShowRow(html, "bortzAgeReductionContainer");
            html = SetContent(html, "lowestBortzAge", PublicHtmlFormat.Fixed(stats.LowestBortzAge));
            html = SetContent(html, "bortzAgeReduction", SignedProfile(stats.BortzAgeReduction));
            html = SetContent(html, "bortzAgeReductionPercent", PublicHtmlFormat.Fixed((1 - stats.BortzPaceOfAging) * 100));
            html = SetContent(html, "bortzAgeReductionAccelerationLabel", stats.BortzAgeReduction < 0 ? "Bortz Age reduction:" : "Bortz Age acceleration:");
        }
        if (stats.CrowdCount > 0 && stats.CrowdAge is double crowdAge && double.IsFinite(crowdAge))
        {
            html = ShowRow(html, "crowdAgeContainer");
            html = ShowRow(html, "notEmptyCrowdAgeContainer");
            html = SetContent(html, "crowdAge", PublicHtmlFormat.Fixed(crowdAge, 0));
            html = SetContent(html, "crowdCount", stats.CrowdCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return html;
    }

    public static string SetContent(string html, string id, string content) => Regex.Replace(html,
        $"(?<opening><(?<tag>[a-z][a-z0-9]*)\\b[^>]*\\bid=\"{Regex.Escape(id)}\"[^>]*>).*?(?<closing></\\k<tag>>)",
        match => match.Groups["opening"].Value + content + match.Groups["closing"].Value, RegexOptions.Singleline);

    private static string ShowRow(string html, string id) => Regex.Replace(html,
        $"(<[^>]*\\bid=\"{Regex.Escape(id)}\"[^>]*?) style=\"display:none;\"", "$1");

    private static string LeagueLink(string label)
    {
        var slug = PublicLeaderboardSnapshot.LeagueFilters.FirstOrDefault(pair => pair.Value.Equals(label, StringComparison.OrdinalIgnoreCase)).Key;
        return slug is null ? Encode(label) : $"<a class=\"league-link\" href=\"/league/{slug}\">{Encode(label)}</a>";
    }

    private static string PopulateLink(string html, string id, string? value)
    {
        var candidate = value?.Trim() ?? "";
        if (!candidate.Contains("://", StringComparison.Ordinal)) candidate = "https://" + candidate;
        var valid = !string.IsNullOrWhiteSpace(value) && !value.TrimStart().StartsWith('@') &&
            Uri.TryCreate(candidate, UriKind.Absolute, out var url) && (url.Scheme == "https" || url.Scheme == "http");
        return html.Replace($"id=\"{id}\" href=\"#\"", valid
            ? $"id=\"{id}\" href=\"{Encode(candidate)}\""
            : $"id=\"{id}\" href=\"#\" style=\"display:none;\"", StringComparison.Ordinal);
    }

    private static string RankingLink(string view, string name, int rank, string href) =>
        $"<a class=\"league-link\" data-competition=\"{view}\" href=\"{href}\" aria-label=\"{Encode(name)}{(view == "ultimate" ? "" : " League")} rank {rank}\"><span>{Encode(name)}</span><strong>#{rank}</strong></a>";

    private static string SignedProfile(double? value) => (value >= 0 ? "+" : "") + PublicHtmlFormat.Fixed(value);
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
