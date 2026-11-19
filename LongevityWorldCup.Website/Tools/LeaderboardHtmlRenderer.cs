using System.Globalization;
using System.Net;
using System.Text;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class LeaderboardHtmlRenderer
{
    public const string ServerRenderedTbodyAttributes = "data-server-rendered=\"true\" aria-busy=\"false\"";

    public static string RenderPodium(LeaderboardSnapshot snapshot, IReadOnlySet<string> visibleSlugs, IReadOnlyList<PublicAthlete> athletes)
    {
        var sb = new StringBuilder();
        foreach (var row in snapshot.Rows.Take(3))
        {
            var rankClass = new[] { "first", "second", "third" }[row.Rank - 1];
            var athlete = athletes.First(a => a.Row.Slug == row.Slug);
            var place = new[] { "1st", "2nd", "3rd" }[row.Rank - 1];
            var blurred = visibleSlugs.Contains(row.Slug) ? "" : " blurred";
            sb.Append($"<div class=\"podium-item {rankClass}{blurred}\" data-athlete-name=\"{EncodeAttribute(row.AthleteName ?? row.DisplayName)}\">")
                .Append($"<div class=\"podium-rank\" title=\"{place} Place\">{PodiumSymbol(rankClass)}</div>")
                .Append($"<a class=\"athlete-profile-link\" href=\"{EncodeAttribute(row.AthletePath)}\" aria-label=\"View stats of {EncodeAttribute(row.DisplayName)}\"><img src=\"{EncodeAttribute(row.LeaderboardThumbnailUrl)}\" alt=\"{EncodeAttribute(row.DisplayName)} portrait\" class=\"podium-portrait\" {PodiumPortraitLoading(row.Rank)}></a>")
                .Append($"<div class=\"name-row\"><a class=\"athlete-name\" href=\"{EncodeAttribute(row.AthletePath)}\" title=\"View stats of {EncodeAttribute(row.DisplayName)}\">{EncodeText(row.DisplayName)}</a></div>")
                .Append(RenderPodiumLinks(athlete))
                .Append($"<div class=\"podium-score\"><span class=\"age-reduction\">{PublicHtmlFormat.Fixed(Math.Abs(row.EffectiveAgeReductionYears ?? 0), row.MetricDecimals)} years</span> reduced</div>")
                .Append("<a class=\"podium-item-lower\" href=\"#contribute\" aria-label=\"Donate to the prize pool\" aria-busy=\"true\"><div class=\"btc-amount\">&nbsp;</div><div class=\"prize-money\">&mdash;</div></a></div>");
        }
        return sb.ToString();
    }

    private static string PodiumSymbol(string name) =>
        $"<svg class=\"podium-symbol\" aria-hidden=\"true\" focusable=\"false\"><use href=\"#podium-{name}\"></use></svg>";

    private static string RenderPodiumLinks(PublicAthlete athlete)
    {
        var row = athlete.Row;
        var name = row.DisplayName;
        var links = new StringBuilder("<div class=\"podium-link-row\">");
        void AddLink(string? href, string kind, string glyph, string label, string title)
        {
            if (href is null) return;
            links.Append($"<a href=\"{EncodeAttribute(href)}\" target=\"_blank\" rel=\"noopener\" class=\"personal-link-icon podium-{kind}-link\" aria-label=\"{EncodeAttribute(label)}\" title=\"{EncodeAttribute(title)}\"")
                .Append(kind == "media" ? " data-contact-glyph" : "").Append('>').Append(PodiumSymbol(glyph)).Append("</a>");
        }

        AddLink(PublicWebUrl(athlete.Source["PersonalLink"]?.ToString()), "personal", "link", "Visit athlete's personal page", $"Visit personal page of {name}");
        var podcastLabel = $"Listen to {name}'s podcast appearance";
        AddLink(PublicWebUrl(athlete.Source["PodcastLink"]?.ToString()), "podcast", "podcast", podcastLabel, podcastLabel);
        var contact = row.MediaContact;
        if (contact.StartsWith('@'))
            links.Append($"<span class=\"media-contact-handle\" title=\"{EncodeAttribute($"Contact {name}")}\">{EncodeText(contact)}</span>");
        else if (PublicWebUrl(contact) is { } href)
            AddLink(href, "media", "contact", $"Contact {name}", $"Contact {name}");
        else if (!string.IsNullOrWhiteSpace(athlete.Source["MediaContact"]?.ToString()))
            // The public HTML omits email addresses. Keep the eventual touch target's footprint.
            links.Append("<span class=\"podium-contact-slot\" aria-hidden=\"true\"></span>");
        return links.Append("</div>").ToString();
    }

    private static string? PublicWebUrl(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || value.StartsWith('@') || value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return null;
        var candidate = value.StartsWith("//", StringComparison.Ordinal) ? "https:" + value
            : value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            && uri.Host.Length > 0 && uri.UserInfo.Length == 0 ? uri.AbsoluteUri : null;
    }

    // The podium is the first thing a visitor looks at: its portraits load with the
    // document instead of waiting for lazy-load layout, the champion first.
    private static string PodiumPortraitLoading(int rank)
        => rank == 1
            ? "loading=\"eager\" fetchpriority=\"high\" decoding=\"async\""
            : "loading=\"eager\" decoding=\"async\"";

    public static string RenderRows(LeaderboardSnapshot snapshot, string metricLabel = "Age reduction", bool separateTracks = true, LeaderboardOrderState? ordering = null)
    {
        ordering ??= new();
        if (snapshot.Rows.Count == 0)
        {
            return $"<tr class=\"no-results\"><td colspan=\"{5 + ordering.Columns.Count()}\"><div class=\"no-results-empty-state\"><p>No athletes match this leaderboard.</p><a href=\"/leaderboard\" class=\"show-all-athletes-btn\">Show all athletes</a></div></td></tr>";
        }

        var sb = new StringBuilder();
        var previousTier = "";
        foreach (var row in snapshot.Rows)
        {
            if (separateTracks && !ordering.IsCustom && previousTier == "pro" && row.Tier == "amateur")
                sb.Append("<tr class=\"tier-separator\" aria-label=\"Start of Amateur track (Pheno Age) section\"><td colspan=\"5\" class=\"tier-separator-cell\"><span class=\"tier-separator-label\">Amateur track |<a href=\"https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/pheno-age.pdf\" target=\"_blank\" rel=\"noopener\" class=\"tier-separator-link\">Pheno Age</a></span></td></tr>");
            AppendRow(sb, row, metricLabel, ordering);
            previousTier = row.Tier;
        }

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, LeaderboardSnapshotRow row, string metricLabel, LeaderboardOrderState ordering)
    {
        var tier = string.Equals(row.Tier, "pro", StringComparison.OrdinalIgnoreCase) ? "pro" : "amateur";
        var rank = row.Rank.ToString(CultureInfo.InvariantCulture);
        var athletePath = EncodeAttribute(row.AthletePath);
        var displayName = EncodeText(row.DisplayName);
        var ageReduction = EncodeText(PublicHtmlFormat.Signed(row.EffectiveAgeReductionYears, row.MetricDecimals) + " years");
        var thumbnail = IsGeneratedAssetUrl(row.LeaderboardThumbnailUrl)
            ? row.LeaderboardThumbnailUrl
            : null;

        sb.Append("<tr id=\"rank-")
            .Append(row.AnchorRank ?? row.Rank)
            .Append("\" class=\"tier-")
            .Append(tier)
            .Append(" server-rendered-leaderboard-row\" data-tier=\"")
            .Append(tier)
            .Append("\" data-athlete-name=\"")
            .Append(EncodeAttribute(row.AthleteName ?? row.DisplayName))
            .Append("\">");
        sb.Append("<td data-label=\"Rank\" class=\"rank-td\"><span class=\"rank\"");
        if (row.Rank is >= 1 and <= 3)
        {
            sb.Append(" data-place=\"").Append(rank).Append('"');
        }
        sb.Append('>')
            .Append(rank)
            .Append("</span></td>");
        sb.Append("<td data-label=\"Athlete\" class=\"athlete-td\">");
        if (!string.IsNullOrWhiteSpace(thumbnail))
        {
            sb.Append("<a class=\"portrait-wrapper athlete-profile-link\" href=\"")
                .Append(athletePath)
                .Append("\" aria-label=\"")
                .Append(EncodeAttribute($"View stats of {row.DisplayName}"))
                .Append("\"><img src=\"")
                .Append(EncodeAttribute(thumbnail))
                .Append("\" alt=\"")
                .Append(EncodeAttribute($"{row.DisplayName} portrait"))
                .Append("\" class=\"portrait\" loading=\"lazy\"></a>");
        }
        sb.Append("<div class=\"athlete-identity\"><a class=\"athlete-name\" href=\"")
            .Append(athletePath)
            .Append("\">")
            .Append(displayName)
            .Append("</a><span class=\"athlete-name-links\"></span></div>");
        sb.Append("<div class=\"badge-section\"></div>");
        sb.Append("</td>");
        sb.Append("<td data-label=\"Sponsor\" class=\"sponsor-td\"></td>");
        sb.Append("<td data-label=\"").Append(EncodeAttribute(metricLabel)).Append("\" class=\"age-reduction-td\"><span class=\"age-reduction\">")
            .Append(ageReduction)
            .Append("</span></td>");
        foreach (var field in ordering.Columns) sb.Append(LeaderboardOrderingHtmlRenderer.Cell(row, field));
        sb.Append("<td data-label=\"Media contact\" class=\"media-contact-td\">")
            .Append(RenderMediaContact(row))
            .Append("</td>");
        sb.Append("</tr>");
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
