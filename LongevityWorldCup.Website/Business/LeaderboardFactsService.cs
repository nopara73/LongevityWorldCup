using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class LeaderboardFactsService(
    IAthleteSnapshotProvider athletes, ContentRevisionStore revisions, EventDataService events, TimeProvider? timeProvider = null)
{
    private const string SiteBaseUrl = SitemapService.SiteBaseUrl;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private string? _snapshotKey;
    private FactsSnapshot? _snapshot;

    public LeaderboardSnapshot GetLeaderboardSnapshot() => Locked(() => GetSnapshot().Leaderboard);

    internal (JsonArray Athletes, LeaderboardSnapshot Leaderboard) Capture() => Locked(() =>
    {
        var snapshot = GetSnapshot();
        return (snapshot.Athletes.DeepClone().AsArray(), snapshot.Leaderboard);
    });

    private T Locked<T>(Func<T> build)
    {
        // Serialize capture, rendering and revision observation so a delayed older
        // request cannot replace a newer document revision.
        lock (_gate) return build();
    }

    public LeaderboardFactsDocument? GetDocumentForPath(string path) => path switch
    {
        "/ai/leaderboard.md" => GetLeaderboardMarkdown(),
        "/ai/athlete-names.md" => GetAthleteNamesMarkdown(),
        "/llms.txt" or "/ai/index.md" => Document(path, "/", AiDiscoveryCatalog.Markdown(false), false),
        "/llms-full.txt" => Document(path, "/", AiDiscoveryCatalog.Markdown(true), false),
        "/.well-known/agent-card.json" => Document(path, "/", AiDiscoveryCatalog.AgentCard(), false),
        _ when path.StartsWith("/ai/league/", StringComparison.Ordinal) && path.EndsWith(".md", StringComparison.Ordinal)
            => GetLeagueMarkdown(path["/ai/league/".Length..^3]),
        _ when path.StartsWith("/ai/athlete/", StringComparison.Ordinal) && path.EndsWith(".md", StringComparison.Ordinal)
            => GetAthleteMarkdown(path["/ai/athlete/".Length..^3]),
        _ => null
    };

    private FactsSnapshot GetSnapshot()
    {
        // One captured dataset drives both the order and its displayed facts. Rebuild on
        // actual data changes or a UTC date boundary, never on an arbitrary cache timeout.
        var source = athletes.GetAthletesSnapshot();
        var day = _clock.GetUtcNow().UtcDateTime.Date;
        var key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ":" +
            PublicGetCacheHeaders.BuildWeakContentETag(source.ToJsonString());
        lock (_gate)
        {
            if (_snapshotKey == key) return _snapshot!;
            var stats = PhenoStatsCalculator.BuildAll(source, day);
            var leaderboard = LeaderboardSnapshotBuilder.Build(AthleteDataService.BuildRankingsOrder(source, day), source, SiteBaseUrl);
            _snapshot = new FactsSnapshot(source, leaderboard, stats, day);
            _snapshotKey = key;
            return _snapshot;
        }
    }

    public LeaderboardFactsDocument GetLeaderboardMarkdown() => Locked(BuildLeaderboardMarkdown);

    private LeaderboardFactsDocument BuildLeaderboardMarkdown()
    {
        var snapshot = GetSnapshot();
        var sb = new StringBuilder("# Longevity World Cup Leaderboard Facts\n\n");
        sb.AppendLine($"Public standings: {SiteBaseUrl}/leaderboard. Current leaders are not declared season winners; completed-season records are at {SiteBaseUrl}/history.");
        sb.AppendLine();
        sb.AppendLine($"Athlete count: {snapshot.Leaderboard.Rows.Count.ToString(CultureInfo.InvariantCulture)}");
        if (snapshot.Leaderboard.Rows.FirstOrDefault() is { } leader)
            sb.AppendLine($"Current Ultimate League leader: {Link(leader.DisplayName, leader.AthleteUrl)}");
        sb.AppendLine();
        AppendDefinitions(sb, snapshot);
        foreach (var view in LeaderboardViewCatalog.Views)
        {
            var rows = LeaderboardViewCatalog.SelectRows(view.HtmlPath, snapshot.Leaderboard.Rows, snapshot.Stats);
            sb.AppendLine($"## {view.Name}");
            sb.AppendLine();
            sb.AppendLine(view.Rules);
            sb.AppendLine();
            sb.AppendLine($"HTML source: {SiteBaseUrl}{view.HtmlPath}. Full machine-readable view: {SiteBaseUrl}{view.MarkdownPath}. Field size: {rows.Count.ToString(CultureInfo.InvariantCulture)}.");
            sb.AppendLine();
            AppendRankingTable(sb, view, rows, snapshot, view.Slug == "ultimate" ? rows.Count : 10);
        }
        return Document("/ai/leaderboard.md", "/leaderboard", sb.ToString());
    }

    public LeaderboardFactsDocument? GetLeagueMarkdown(string slug) => Locked(() => BuildLeagueMarkdown(slug));

    private LeaderboardFactsDocument? BuildLeagueMarkdown(string slug)
    {
        var view = LeaderboardViewCatalog.Find(slug);
        if (view is null) return null;
        if (view.Slug == "ultimate") return GetLeaderboardMarkdown();
        var snapshot = GetSnapshot();
        var rows = LeaderboardViewCatalog.SelectRows(view.HtmlPath, snapshot.Leaderboard.Rows, snapshot.Stats);
        var sb = new StringBuilder($"# {view.Name}\n\n");
        sb.AppendLine($"Canonical HTML source: {SiteBaseUrl}{view.HtmlPath}");
        sb.AppendLine();
        sb.AppendLine(view.Rules);
        sb.AppendLine();
        sb.AppendLine($"Field size: {rows.Count.ToString(CultureInfo.InvariantCulture)}. Current standings, not a completed-season result.");
        sb.AppendLine();
        AppendDefinitions(sb, snapshot);
        AppendRankingTable(sb, view, rows, snapshot, rows.Count);
        return Document(view.MarkdownPath, view.HtmlPath, sb.ToString());
    }

    public LeaderboardFactsDocument GetAthleteNamesMarkdown() => Locked(BuildAthleteNamesMarkdown);

    private LeaderboardFactsDocument BuildAthleteNamesMarkdown()
    {
        var sb = new StringBuilder();
        foreach (var row in GetSnapshot().Leaderboard.Rows)
            sb.AppendLine($"{row.Rank.ToString(CultureInfo.InvariantCulture)}. {Text(row.DisplayName)}");
        return Document("/ai/athlete-names.md", "/leaderboard", sb.ToString(), includeMetadata: false);
    }

    public LeaderboardFactsDocument? GetAthleteMarkdown(string slug) => Locked(() => BuildAthleteMarkdown(slug));

    private LeaderboardFactsDocument? BuildAthleteMarkdown(string slug)
    {
        var snapshot = GetSnapshot();
        var canonicalSlug = AthleteSlug.Normalize(slug);
        var row = snapshot.Leaderboard.Rows.FirstOrDefault(row => row.Slug == canonicalSlug);
        if (row is null) return null;
        var athlete = snapshot.Athletes.OfType<JsonObject>().Single(athlete => athlete["AthleteSlug"]?.GetValue<string>() == canonicalSlug);
        var stats = snapshot.Stats[row.Slug];
        var sb = new StringBuilder($"# {Text(row.DisplayName)}\n\n");
        sb.AppendLine($"Canonical HTML source: {row.AthleteUrl}");
        sb.AppendLine();
        sb.AppendLine($"Track: {row.Track}. Division: {Value(row.Division)}. Generation: {Value(row.Generation)}. Flag: {Value(row.Flag)}. Exclusive league: {Value(row.ExclusiveLeague)}.");
        sb.AppendLine();
        sb.AppendLine($"Current chronological age: {Number(stats.ChronoAge)} years, as of {snapshot.AsOf:yyyy-MM-dd} UTC.");
        sb.AppendLine($"Crowd age: {(stats.CrowdCount > 0 ? Number(stats.CrowdAge) : "Not available")} years; accepted guesses for the current image: {stats.CrowdCount.ToString(CultureInfo.InvariantCulture)}; Crowd Age League qualification: {(stats.CrowdCount >= 100 && stats.CrowdAge is { } crowdAge && double.IsFinite(crowdAge) ? "qualified" : "not qualified (100 guesses required)")}.");
        sb.AppendLine();
        if (athlete["Why"]?.GetValue<string>() is { Length: > 0 } why)
        {
            sb.AppendLine("## Athlete-provided biography");
            sb.AppendLine();
            sb.AppendLine(Text(why));
            sb.AppendLine();
        }
        sb.AppendLine("## Current rankings");
        sb.AppendLine();
        sb.AppendLine("| View | Rank in view | Field size | Score (years) | HTML source |");
        sb.AppendLine("| --- | ---: | ---: | ---: | --- |");
        foreach (var view in LeaderboardViewCatalog.Views)
        {
            var rows = LeaderboardViewCatalog.SelectRows(view.HtmlPath, snapshot.Leaderboard.Rows, snapshot.Stats);
            var rank = rows.Select((candidate, index) => (candidate.Slug, Rank: index + 1)).FirstOrDefault(item => item.Slug == row.Slug).Rank;
            if (rank == 0) continue;
            sb.AppendLine($"| {view.Name} | {rank} | {rows.Count} | {Number(LeaderboardViewCatalog.Metric(view.HtmlPath, row, stats), signed: true)} | {SiteBaseUrl}{view.HtmlPath} |");
        }
        sb.AppendLine();
        sb.AppendLine("A missing view means the athlete is outside that field or does not qualify. Rankings are current, not historical placements. See each view's machine document for its metric and tie breakers.");
        sb.AppendLine();
        sb.AppendLine("## Public result history");
        sb.AppendLine();
        sb.AppendLine("Test date is the laboratory measurement date. First public announcement is recorded only where a public accepted-result Event exists; older untracked publication dates remain unavailable. Historical test rows are not historical ranks.");
        sb.AppendLine();
        sb.AppendLine("| Test date | First public announcement (UTC) | Pheno age | Bortz age |");
        sb.AppendLine("| --- | --- | ---: | ---: |");
        var acceptedEvents = events.GetEvents(type: EventType.TestResultAccepted, visibleOnWebsite: true, toUtc: _clock.GetUtcNow().UtcDateTime)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var test in (athlete["Biomarkers"]?.AsArray() ?? []).OfType<JsonObject>()
            .OrderBy(test => test["Date"]?.GetValue<string>(), StringComparer.Ordinal))
        {
            var testDate = test["Date"]?.GetValue<string>();
            if (!DateOnly.TryParse(testDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                sb.AppendLine("| Not available | Not available | Not available | Not available |");
                continue;
            }
            var isolated = new JsonObject { ["DateOfBirth"] = athlete["DateOfBirth"]?.DeepClone(), ["Biomarkers"] = new JsonArray(test.DeepClone()) };
            var result = PhenoStatsCalculator.Compute(isolated, snapshot.AsOf);
            acceptedEvents.TryGetValue($"accepted-result:{row.Slug}:{date:yyyy-MM-dd}", out var publication);
            sb.AppendLine($"| {date:yyyy-MM-dd} | {(publication is null ? "Not available" : publication.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture))} | {(result.LowestPhenoAgeDateUtc.HasValue ? Number(result.LowestPhenoAge) : "Not available")} | {(result.BortzSubmissionCount > 0 ? Number(result.LowestBortzAge) : "Not available")} |");
        }
        sb.AppendLine();
        sb.AppendLine("Scores are derived from submitted public biomarkers using the site's current calculators. Missing or incomplete panels have unavailable clock results. These measures do not establish a treatment's causal effect or years of life gained.");
        return Document($"/ai/athlete/{row.RouteSlug}.md", row.AthletePath, sb.ToString());
    }

    internal LeaderboardFactsDocument Document(string path, string source, string body, bool includeMetadata = true)
    {
        body = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var changedAt = revisions.Observe("document:" + path, PublicGetCacheHeaders.BuildWeakContentETag(body), _clock.GetUtcNow());
        var metadata = includeMetadata
            ? $"---\ncanonical: {SiteBaseUrl}{path}\nsource: {SiteBaseUrl}{source}\nfacts_changed_at_utc: {(changedAt.HasValue ? changedAt.Value.ToString("O", CultureInfo.InvariantCulture) : "unknown")}\n---\n\n"
            : "";
        return new LeaderboardFactsDocument(metadata + body, changedAt);
    }

    private static void AppendDefinitions(StringBuilder sb, FactsSnapshot snapshot)
    {
        sb.AppendLine("## Field definitions and dates");
        sb.AppendLine();
        sb.AppendLine("- Scores are in years; lower and more negative values rank higher. Sorting uses unrounded scores; tables show two decimals. Rank is within the named view; Ultimate rank is a separate column.");
        sb.AppendLine("- Age reduction is biological age minus chronological age at the selected test. Crowd age reduction uses the current-image median minus current chronological age.");
        sb.AppendLine("- Lowest clock ages and their test dates refer to that clock's selected result. Not available means no eligible value was recorded, not zero.");
        sb.AppendLine($"- Current chronological age and Crowd Age comparisons are evaluated as of {snapshot.AsOf:yyyy-MM-dd} UTC.");
        sb.AppendLine("- facts_changed_at_utc records an observed change to this document's facts. Unknown means no historical change date has been verified. Cache refresh and response-generation times are not publication or test dates.");
        sb.AppendLine($"- Competition rules: {SiteBaseUrl}/ruleset. Completed-season results: {SiteBaseUrl}/history.");
        sb.AppendLine();
    }

    private static void AppendRankingTable(StringBuilder sb, LeaderboardViewDefinition view,
        IReadOnlyList<LeaderboardSnapshotRow> rows, FactsSnapshot snapshot, int limit)
    {
        sb.AppendLine($"Score: {view.Metric}. Showing {Math.Min(limit, rows.Count)} of {rows.Count} athletes.");
        sb.AppendLine();
        sb.AppendLine("| Rank in view | Ultimate rank | Athlete | Track | Score (years) | Lowest Bortz Age | Bortz test date | Lowest Pheno Age | Pheno test date | Crowd age | Crowd count | Athlete summary |");
        sb.AppendLine("| ---: | ---: | --- | --- | ---: | ---: | --- | ---: | --- | ---: | ---: | --- |");
        for (var i = 0; i < Math.Min(rows.Count, limit); i++)
        {
            var row = rows[i];
            var stats = snapshot.Stats[row.Slug];
            sb.AppendLine($"| {i + 1} | {row.Rank} | {Link(row.DisplayName, row.AthleteUrl)} | {row.Track} | {Number(LeaderboardViewCatalog.Metric(view.HtmlPath, row, stats), signed: true)} | {(stats.BortzSubmissionCount > 0 ? Number(stats.LowestBortzAge) : "Not available")} | {Date(stats.LowestBortzAgeDateUtc)} | {(stats.LowestPhenoAgeDateUtc.HasValue ? Number(stats.LowestPhenoAge) : "Not available")} | {Date(stats.LowestPhenoAgeDateUtc)} | {(stats.CrowdCount > 0 ? Number(stats.CrowdAge) : "Not available")} | {stats.CrowdCount} | {SiteBaseUrl}/ai/athlete/{row.RouteSlug}.md |");
        }
        if (rows.Count == 0) sb.AppendLine("\nNo athletes currently qualify for this view.");
        sb.AppendLine();
    }

    private static string Link(string text, string url) => $"[{Text(text)}]({url})";
    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "Not available" : Text(value);
    private static string Date(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Not available";
    private static string Number(double? value, bool signed = false) => value.HasValue && double.IsFinite(value.Value)
        ? value.Value.ToString(signed ? "+0.00;-0.00;0.00" : "0.00", CultureInfo.InvariantCulture) : "Not available";
    internal static string Text(string? value) => (value ?? "").Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)
        .Replace("|", "\\|", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Trim();

    private sealed record FactsSnapshot(JsonArray Athletes, LeaderboardSnapshot Leaderboard,
        IReadOnlyDictionary<string, PhenoStatsCalculator.Result> Stats, DateTime AsOf);
}

public sealed record LeaderboardFactsDocument(string Markdown, DateTimeOffset? LastModifiedUtc);
