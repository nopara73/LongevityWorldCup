using System.Net;
using System.Text;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class FlagLeaderboardHtmlRenderer
{
    public static string Render(IEnumerable<FlagLeaderboardTeam> teams, bool separateClocks = false)
    {
        var html = new StringBuilder();
        var previousSection = "";
        foreach (var team in teams)
        {
            var section = team.Rank.HasValue ? separateClocks ? team.View : "" : "incomplete";
            var isAmateur = section == "pheno";
            if (isAmateur && previousSection == "bortz")
                html.Append("<h3 class=\"flag-section-heading tier-separator tier-separator-cell\" aria-label=\"Start of Amateur track (Pheno Age) section\"><span class=\"tier-separator-label\">Amateur track |<a href=\"https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/pheno-age.pdf\" target=\"_blank\" rel=\"noopener\" class=\"tier-separator-link\">Pheno Age</a></span></h3>");
            else if (section == "incomplete" && section != previousSection)
                html.Append("<h3 class=\"flag-section-heading\">Incomplete teams</h3>");
            previousSection = section;
            var rank = team.Rank?.ToString() ?? "—";
            html.Append($"<details class=\"flag-team{(isAmateur ? " tier-amateur" : "")}\" id=\"flag-team-{team.Flag.Slug}\" data-clock=\"{team.View}\" data-flag=\"{Encode(team.Flag.Name)}\">")
                .Append($"<summary title=\"{FlagLeaderboard.Label(team.View)}\"><span class=\"flag-team-rank\" data-rank=\"{team.Rank?.ToString() ?? ""}\" aria-label=\"{(team.Rank.HasValue ? "Rank " + rank : "Unranked")}\">{rank}</span>")
                .Append($"<span class=\"flag-team-name\"><span class=\"flag-team-icon\" aria-hidden=\"true\">{Icon(team.Flag.Name)}</span><span>{Encode(team.Flag.Name)}</span></span>")
                .Append("<span class=\"flag-team-portraits\" aria-hidden=\"true\">");
            foreach (var athlete in team.Athletes)
                html.Append($"<img src=\"{Portrait(athlete)}\" data-athlete-name=\"{Encode(athlete.DisplayName)}\" alt=\"\" loading=\"lazy\">");
            html.Append($"</span><span class=\"flag-team-score\">{(team.Rank.HasValue ? PublicHtmlFormat.Signed(team.Score, 2) : team.Athletes.Count + "/3")} <small>{(team.Rank.HasValue ? "years total" : "to qualify")}</small></span><span class=\"flag-team-chevron\" aria-hidden=\"true\">⌄</span></summary><ol class=\"flag-team-athletes\" aria-label=\"Top {FlagLeaderboard.Label(team.View)} athletes for {Encode(team.Flag.Name)}\">");
            var place = 0;
            foreach (var athlete in team.Athletes)
            {
                place++;
                html.Append($"<li><a class=\"flag-athlete-link\" href=\"{Encode(athlete.AthletePath)}\"><span class=\"flag-athlete-rank\" title=\"Rank within this team\">{place}</span>")
                    .Append($"<img src=\"{Portrait(athlete)}\" alt=\"\" loading=\"lazy\"><span class=\"flag-athlete-name\">{Encode(athlete.DisplayName)}<small>{athlete.Track}</small></span>")
                    .Append($"<span class=\"flag-athlete-metric\">{PublicHtmlFormat.Signed(athlete.EffectiveAgeReductionYears, 1)} <small>years</small></span></a></li>");
            }
            html.Append("</ol></details>");
        }
        return html.Length > 0 ? html.ToString() : "<p class=\"flag-empty\">No flags match this leaderboard.</p>";
    }

    private static string Icon(string name) => FlagIconCatalog.GetCode(name) is { } code
        ? $"<span class=\"fi fi-{code}\"></span>" : "<i class=\"fas fa-flag\"></i>";

    private static string Portrait(LeaderboardSnapshotRow athlete) => string.IsNullOrWhiteSpace(athlete.LeaderboardThumbnailUrl)
        ? "{{ASSET_HD_LOGO_THUMB_SM}}" : Encode(athlete.LeaderboardThumbnailUrl);

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? "");
}
