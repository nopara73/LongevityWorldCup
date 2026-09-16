using System.Text.Json;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business.IndexNow;

public sealed class IndexNowContentSnapshot(
    LeaderboardFactsService facts,
    EventDataService events,
    LongevitymaxxingChallengeService challenge,
    IndexNowPageContent pages,
    TimeProvider? timeProvider = null)
{
    public const string SiteBaseUrl = SitemapService.SiteBaseUrl;
    private static readonly string[] ProfileFields =
    [
        "Name", "DisplayName", "DateOfBirth", "Division", "Generation", "ExclusiveLeague", "Flag",
        "Why", "PersonalLink", "PodcastLink", "ProfileImageId", "Biomarkers", "Badges", "Placements", "IndexNowProofHash"
    ];

    public IReadOnlyDictionary<string, string> Build(CancellationToken cancellationToken, bool trackEveryPublicChange = false)
    {
        var (snapshot, leaderboard) = facts.Capture();
        foreach (var athlete in snapshot.OfType<JsonObject>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            athlete["IndexNowProofHash"] = pages.HashProofs(athlete["Proofs"]?.AsArray() ?? []);
        }
        var paths = SitemapService.StaticRoutes.Select(route => route.Path)
            .Concat(SitemapService.PublicLeaguePaths)
            .Concat(FlagRouteCatalog.BuildRoutes(leaderboard.Rows.Select(row => row.Flag)).Select(flag => flag.Path))
            .Concat(leaderboard.Rows.Select(row => row.AthletePath));
        var pageHashes = pages.Build(paths, cancellationToken);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var result = Build(snapshot, leaderboard, events.GetEvents(visibleOnWebsite: true, toUtc: now.UtcDateTime),
            pageHashes, JsonSerializer.SerializeToNode(challenge.GetPublicState(now)), now.UtcDateTime, trackEveryPublicChange).ToDictionary();
        // Machine documents include all advertised views and use exact content validators.
        // Their dependencies are broader than the Ultimate-only HTML leaderboard.
        foreach (var route in SitemapService.StaticRoutes)
            if (facts.GetDocumentForPath(route.Path) is { } document)
                result[SiteBaseUrl + route.Path] = IndexNowPageContent.Hash(document.Markdown);
        return result;
    }

    internal static IReadOnlyDictionary<string, string> Build(JsonArray athletes, LeaderboardSnapshot leaderboard,
        IReadOnlyList<EventItem> events, IReadOnlyDictionary<string, string> pageHashes, JsonNode? challenge, DateTime asOf,
        bool trackEveryPublicChange = false)
    {
        var source = athletes.OfType<JsonObject>().ToDictionary(a => a["AthleteSlug"]!.GetValue<string>(), StringComparer.Ordinal);
        var stats = PhenoStatsCalculator.BuildAll(athletes, asOf.Date);
        var viewRanks = new[] { "/league/bortz", "/league/pheno", "/league/improvement", "/league/bortz-improvement", "/league/crowd" }
            .ToDictionary(path => path, path => SelectRows(path, leaderboard.Rows, stats)
                .Select((row, index) => (row.Slug, Rank: index + 1)).ToDictionary(row => row.Slug, row => row.Rank));
        var publicEvents = events.Where(e => e.VisibleOnWebsite && e.OccurredAtUtc <= asOf).OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        var sharedEvents = publicEvents.Where(e => IsSharedEvent(e, leaderboard.Rows, homepage: false))
            .Select(e => new
            {
                Event = e,
                Athletes = EventDataService.ExtractReferencedAthleteSlugs(e.Text).Order(StringComparer.Ordinal).Select(slug =>
                {
                    var row = leaderboard.Rows.FirstOrDefault(row => row.Slug == slug);
                    return new { Slug = slug, row?.DisplayName, row?.Rank, Image = source.GetValueOrDefault(slug)?["ProfileImageId"]?.GetValue<string>() };
                }).ToArray()
            }).ToArray();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (path, pageHash) in pageHashes)
        {
            object? content = null;
            if (path.StartsWith("/athlete/", StringComparison.Ordinal))
            {
                var row = leaderboard.Rows.FirstOrDefault(row => row.AthletePath == path);
                if (row is null || !source.TryGetValue(row.Slug, out var athlete)) continue;
                var profile = new JsonObject(ProfileFields.Select(field => KeyValuePair.Create(field,
                    field is "Biomarkers" or "Badges" && athlete[field] is JsonArray array
                        ? new JsonArray(array.Select(Normalize).OrderBy(value => value?.ToJsonString(), StringComparer.Ordinal).ToArray())
                        : Normalize(athlete[field]))));
                profile["MediaContact"] = row.MediaContact;
                // Proof versions use file timestamps. Names/presence are public profile content;
                // these storage URLs never enter the notification URL list.
                profile["Proofs"] = new JsonArray((athlete["Proofs"]?.AsArray() ?? [])
                    .Select(proof => (JsonNode?)JsonValue.Create(proof?.GetValue<string>().Split('?')[0])).ToArray());
                content = new { profile, row.Rank, row.Track, Score = Rounded(row.EffectiveAgeReductionYears),
                    OtherRanks = viewRanks.ToDictionary(view => view.Key, view => view.Value.GetValueOrDefault(row.Slug)),
                    Age = stats[row.Slug].ChronoAge is { } age ? (int)age : (int?)null,
                    CrowdAge = Rounded(stats[row.Slug].CrowdAge),
                    CrowdCount = CrowdCountBucket(stats[row.Slug].CrowdCount),
                    PublicFacts = trackEveryPublicChange ? PublicFacts(stats[row.Slug]) : null,
                    Events = publicEvents.Where(e => EventDataService.ExtractReferencedAthleteSlugs(e.Text).Contains(row.Slug, StringComparer.Ordinal)).ToArray() };
            }
            else if (path == "/" || path == "/leaderboard" || path.StartsWith("/league/", StringComparison.Ordinal) || path.StartsWith("/flag/", StringComparison.Ordinal) || path == "/ai/leaderboard.md")
            {
                var rows = SelectRows(path, leaderboard.Rows, stats).Select(row => new
                {
                    row.Slug, row.DisplayName, row.Track, row.Division, row.Generation, row.Flag, row.ExclusiveLeague,
                    row.MediaContact, Image = source.GetValueOrDefault(row.Slug)?["ProfileImageId"]?.GetValue<string>(),
                    Metric = Metric(path, row, stats[row.Slug]),
                    PublicFacts = trackEveryPublicChange ? PublicFacts(stats[row.Slug]) : null,
                    CrowdCount = path == "/league/crowd" ? CrowdCountBucket(stats[row.Slug].CrowdCount) : (int?)null
                }).ToArray();
                content = new { Rows = rows, Events = path == "/" ? sharedEvents.Where(e => IsSharedEvent(e.Event, leaderboard.Rows, homepage: true)).ToArray() : null };
            }
            else if (path == "/ai/athlete-names.md")
                content = leaderboard.Rows.Select(row => new { row.Slug, row.DisplayName }).ToArray();
            else if (path == "/events")
                content = sharedEvents;
            else if (path is "/longevitymaxxing" or "/helstab-kihivas")
                content = challenge;

            var url = SiteBaseUrl + new PathString(path).ToUriComponent();
            if (IsCanonicalUrl(url)) hashes[url] = Fingerprint(new { Page = pageHash, Content = content });
        }
        return hashes;
    }

    // The source is a closed catalog, never URLs supplied by a request, event text, or athlete link.
    // This syntax guard also permits previously eligible athlete/flag URLs after their removal.
    internal static bool IsCanonicalUrl(string url)
    {
        if (!IsCanonicalHostUrl(url)) return false;
        var uri = new Uri(url);
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (SitemapService.StaticRoutes.Any(route => route.Path == path) || SitemapService.PublicLeaguePaths.Contains(path))
            return true;
        var segments = path.Split('/');
        return segments.Length == 3 && segments[1] is "athlete" or "flag" && segments[2].Length > 0 &&
            segments[2].Split('-').All(segment => segment.Length > 0 && segment.All(c => char.IsLetterOrDigit(c) && !char.IsUpper(c))) &&
            SiteBaseUrl + new PathString(path).ToUriComponent() == url;
    }

    internal static bool IsCanonicalHostUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && url.StartsWith(SiteBaseUrl + "/", StringComparison.Ordinal) &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsoluteUri == url;

    // Match the shared event board's profile-only and main-feed filters. This is a
    // content dependency projection; it never changes Event visibility or dispatch.
    private static bool IsSharedEvent(EventItem item, IReadOnlyList<LeaderboardSnapshotRow> rows, bool homepage)
    {
        if (item.Type is EventType.TestResultAccepted or EventType.SeasonFinalResult) return false;
        if (item.Type == EventType.NewRank)
        {
            if (EventHelpers.TryExtractRank(item.Text, out var rank)) return rank <= 10;
            return EventHelpers.TryExtractSlug(item.Text, out var slug) && rows.Any(row => row.Slug == slug && row.Rank <= 10);
        }
        if (item.Type == EventType.LongevitymaxxingChallengeResult && homepage)
            return EventHelpers.TryExtractPlace(item.Text, out var challengePlace) && challengePlace is >= 1 and <= 3;
        if (item.Type != EventType.BadgeAward) return true;
        if (!EventHelpers.TryExtractBadgeLabel(item.Text, out var label)) return false;
        label = System.Text.RegularExpressions.Regex.Replace(label.Replace('–', '-').Replace('—', '-'), @"\s+", " ").Trim().ToLowerInvariant();
        if (label is "podcast" or "pregnancy") return true;
        if (!EventHelpers.TryExtractPlace(item.Text, out var place) || place is < 1 or > 3) return false;
        if (label == "age reduction")
            return EventHelpers.TryExtractCategory(item.Text, out var category) &&
                category.ToLowerInvariant() is "global" or "generation" or "division" or "exclusive" or "amateur";
        if (label is "chronological age - oldest" or "chronological age - youngest" or "phenoage - lowest" or
            "bortz age - lowest" or "phenoage best improvement" or "bortz age best improvement") return true;
        return !homepage && (label is "pheno pace of aging" or "bortz pace of aging" || label.StartsWith("best domain -", StringComparison.Ordinal));
    }

    private static IReadOnlyList<LeaderboardSnapshotRow> SelectRows(string path, IReadOnlyList<LeaderboardSnapshotRow> rows,
        IReadOnlyDictionary<string, PhenoStatsCalculator.Result> stats) => LeaderboardViewCatalog.SelectRows(path, rows, stats);

    private static double? Metric(string path, LeaderboardSnapshotRow row, PhenoStatsCalculator.Result stats) => Rounded(path switch
    {
        "/league/pheno" => stats.AgeReduction,
        "/league/improvement" => stats.PhenoAgeImprovementFromWorst,
        "/league/bortz-improvement" => stats.BortzAgeImprovementFromWorst,
        "/league/crowd" => stats.CrowdAge, // Excludes daily passage of time; real guesses change this median.
        _ => row.EffectiveAgeReductionYears
    });

    private static double? Rounded(double? value) => value.HasValue && double.IsFinite(value.Value) ? Math.Round(value.Value, 2) : null;
    // Freshness tracks public facts even when the notification policy deliberately
    // coalesces small changes, such as an additional guess within a count bucket.
    private static object PublicFacts(PhenoStatsCalculator.Result stats) => new
    {
        PhenoAge = stats.LowestPhenoAgeDateUtc.HasValue ? Finite(stats.LowestPhenoAge) : null,
        BortzAge = stats.BortzSubmissionCount > 0 ? Finite(stats.LowestBortzAge) : null,
        stats.LowestPhenoAgeDateUtc, stats.LowestBortzAgeDateUtc,
        AgeReduction = Finite(stats.AgeReduction), BortzAgeReduction = Finite(stats.BortzAgeReduction),
        PhenoImprovement = Finite(stats.PhenoAgeImprovementFromWorst), BortzImprovement = Finite(stats.BortzAgeImprovementFromWorst),
        stats.SubmissionCount, stats.BortzSubmissionCount,
        CrowdAge = Finite(stats.CrowdAge), CrowdAgeReduction = Rounded(stats.CrowdAge - stats.ChronoAge), stats.CrowdCount
    };
    private static double? Finite(double? value) => value.HasValue && double.IsFinite(value.Value) ? value : null;
    private static int CrowdCountBucket(int count) => count < 20 ? count : count < 100 ? count / 5 * 5 : count < 1000 ? count / 10 * 10 : count / 100 * 100;
    internal static string Fingerprint(object? content) => IndexNowPageContent.Hash(Normalize(JsonSerializer.SerializeToNode(content))?.ToJsonString() ?? "null");

    private static JsonNode? Normalize(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => KeyValuePair.Create(pair.Key, Normalize(pair.Value)))),
        JsonArray array => new JsonArray(array.Select(Normalize).ToArray()),
        _ => node?.DeepClone()
    };
}
