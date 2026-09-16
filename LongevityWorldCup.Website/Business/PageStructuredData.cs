using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

/// <summary>Public page subjects only. Ranking and profile facts come from the published athlete data.</summary>
public sealed class PageStructuredData(AthleteDataService athletes)
{
    public const string SiteUrl = "https://longevityworldcup.com";
    private static readonly string[] Views = ["ultimate", "bortz", "pheno", "improvement", "bortz-improvement", "crowd"];
    private static readonly Dictionary<string, string> LeagueFilters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["amateur"] = "Amateur", ["professional"] = "Professional",
        ["mens"] = "Men's", ["womens"] = "Women's", ["open"] = "Open",
        ["silent-generation"] = "Silent Generation", ["baby-boomers"] = "Baby Boomers",
        ["gen-x"] = "Gen X", ["millennials"] = "Millennials", ["gen-z"] = "Gen Z",
        ["gen-alpha"] = "Gen Alpha", ["prosperan"] = "Prosperan"
    };

    public void AddSubject(List<object> graph, Dictionary<string, object> page,
        string canonicalPath, string canonicalUrl, string requestPath, IQueryCollection query)
    {
        Dictionary<string, object>? subject = null;
        if (canonicalPath.StartsWith("/athlete/", StringComparison.Ordinal))
        {
            var slug = canonicalPath["/athlete/".Length..];
            var athlete = athletes.GetAthletesSnapshot().OfType<JsonObject>()
                .FirstOrDefault(a => RouteSlug(Text(a, "AthleteSlug")) == slug);
            if (athlete is null) return;

            page["@type"] = "ProfilePage";
            subject = Person(Text(athlete, "AthleteSlug"), DisplayName(athlete));
            subject["mainEntityOfPage"] = Reference(canonicalUrl + "#webpage");
            // Use the public versioned portrait, never the social share-card composition.
            var portrait = Text(athlete, "ProfilePic");
            if (portrait.StartsWith('/') && !portrait.StartsWith("//", StringComparison.Ordinal))
            {
                subject["image"] = SiteUrl + portrait;
                page["primaryImageOfPage"] = new Dictionary<string, object>
                {
                    ["@type"] = "ImageObject", ["url"] = SiteUrl + portrait
                };
            }
            else page.Remove("primaryImageOfPage");
        }
        else if (requestPath == "/leaderboard" || requestPath.StartsWith("/league/", StringComparison.Ordinal)
                 || requestPath.StartsWith("/flag/", StringComparison.Ordinal))
        {
            var snapshot = athletes.GetAthletesSnapshot().OfType<JsonObject>().ToList();
            if (!TryGetSelection(requestPath, query, snapshot, out var view, out var filters)) return;
            page["@type"] = "CollectionPage";

            // Search includes rendered badge text and display precision. The browser adds its
            // exact matching entries after hydration rather than publishing an unfiltered list.
            if (!string.IsNullOrWhiteSpace(query["search"])) return;
            var bySlug = snapshot.ToDictionary(a => Text(a, "AthleteSlug"), StringComparer.OrdinalIgnoreCase);
            var ordered = athletes.GetLeagueSlugsInRankOrder(view)
                .Where(bySlug.ContainsKey).Select(slug => bySlug[slug]).ToList();
            var proSlugs = athletes.GetLeagueSlugsInRankOrder("bortz").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selected = Filter(ordered, snapshot, filters, proSlugs);
            var items = selected.Select((athlete, index) => new Dictionary<string, object>
            {
                ["@type"] = "ListItem", ["position"] = index + 1,
                ["item"] = Person(Text(athlete, "AthleteSlug"), DisplayName(athlete))
            }).ToArray();
            var selectionUrl = SelectionUrl(view, filters);
            subject = new Dictionary<string, object>
            {
                ["@type"] = "ItemList", ["@id"] = selectionUrl + "#ranking", ["url"] = selectionUrl,
                ["name"] = query.ContainsKey("filters") || query.ContainsKey("view")
                    ? "Longevity World Cup rankings" : page["name"],
                ["itemListOrder"] = "https://schema.org/ItemListOrderAscending",
                ["numberOfItems"] = items.Length, ["itemListElement"] = items
            };
        }
        else if (canonicalPath == "/history")
        {
            subject = new Dictionary<string, object>
            {
                ["@type"] = "Article", ["@id"] = canonicalUrl + "#article", ["url"] = canonicalUrl,
                ["headline"] = "History of longevity as a sport",
                ["inLanguage"] = "en", ["publisher"] = Reference(SiteUrl + "/#organization"),
                ["mainEntityOfPage"] = Reference(canonicalUrl + "#webpage")
            };
        }
        else if (canonicalPath == "/about") page["@type"] = "AboutPage";
        else if (canonicalPath == "/media") page["@type"] = "CollectionPage";

        if (subject is null) return;
        page["mainEntity"] = Reference((string)subject["@id"]);
        page["about"] = Reference((string)subject["@id"]);
        graph.Add(subject);
    }

    public static Dictionary<string, object> Person(string slug, string name)
    {
        var url = SiteUrl + "/athlete/" + Uri.EscapeDataString(RouteSlug(slug));
        return new Dictionary<string, object>
        {
            ["@type"] = "Person", ["@id"] = url + "#person", ["url"] = url, ["name"] = name
        };
    }

    private static bool TryGetSelection(string path, IQueryCollection query, List<JsonObject> snapshot,
        out string view, out string[] filters)
    {
        view = "ultimate";
        filters = [];
        if (path.StartsWith("/league/", StringComparison.Ordinal))
        {
            var league = path["/league/".Length..];
            if (league == "pheno-improvement") league = "improvement";
            if (Views.Contains(league)) view = league;
            else if (LeagueFilters.TryGetValue(league, out var label)) filters = [label];
            else return false;
        }
        else if (path.StartsWith("/flag/", StringComparison.Ordinal))
        {
            if (!FlagRouteCatalog.TryResolve(path["/flag/".Length..], snapshot.Select(a => Text(a, "Flag")), out var flag))
                return false;
            filters = [flag.Name];
        }
        var requestedView = query["view"].FirstOrDefault();
        if (!string.IsNullOrEmpty(requestedView))
        {
            requestedView = requestedView.ToLowerInvariant();
            if (requestedView == "pheno-improvement") requestedView = "improvement";
            view = Views.Contains(requestedView) ? requestedView : "ultimate";
        }
        var requestedFilters = query["filters"].FirstOrDefault();
        if (!string.IsNullOrEmpty(requestedFilters))
            filters = requestedFilters.Split(',').Select(DecodeFilter).Where(f => f.Length > 0).ToArray();
        var tokens = filters.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var available = snapshot.Select(a => Text(a, "Division"))
            .Concat(snapshot.Select(a => GenerationResolver.ResolveFromAthleteJson(a) ?? ""))
            .Concat(["Professional", "Amateur", "Prosperan"]);
        var flagSlugs = filters.Select(f => FlagRouteCatalog.TryCreate(f, out var flag) ? flag.Slug : "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        filters = available.Where(value => tokens.Contains(value) && value.Length > 0)
            .Concat(FlagRouteCatalog.BuildRoutes(snapshot.Select(a => Text(a, "Flag")))
                .Where(flag => flagSlugs.Contains(flag.Slug)).Select(flag => flag.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tokens.Contains("Professional") && tokens.Contains("Amateur"))
            filters = filters.Where(value => value is not ("Professional" or "Amateur")).ToArray();
        return true;
    }

    private static IEnumerable<JsonObject> Filter(List<JsonObject> ordered, List<JsonObject> snapshot,
        string[] filters, HashSet<string> proSlugs)
    {
        var tokens = filters.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var divisions = snapshot.Select(a => Text(a, "Division")).Where(tokens.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var generations = snapshot.Select(a => GenerationResolver.ResolveFromAthleteJson(a) ?? "").Where(tokens.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var flagSlugs = filters.Select(f => FlagRouteCatalog.TryCreate(f, out var flag) ? flag.Slug : "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedFlags = FlagRouteCatalog.BuildRoutes(snapshot.Select(a => Text(a, "Flag")))
            .Where(f => flagSlugs.Contains(f.Slug)).Select(f => f.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pro = tokens.Contains("Professional");
        var amateur = tokens.Contains("Amateur");
        return ordered.Where(a =>
            (pro == amateur || proSlugs.Contains(Text(a, "AthleteSlug")) == pro) &&
            (divisions.Count == 0 || divisions.Contains(Text(a, "Division"))) &&
            (generations.Count == 0 || generations.Contains(GenerationResolver.ResolveFromAthleteJson(a) ?? "")) &&
            (!tokens.Contains("Prosperan") || Text(a, "ExclusiveLeague").Equals("Prosperan", StringComparison.OrdinalIgnoreCase)) &&
            (selectedFlags.Count == 0 || FlagRouteCatalog.TryCreate(Text(a, "Flag"), out var flag) && selectedFlags.Contains(flag.Slug)));
    }

    private static string SelectionUrl(string view, string[] filters)
    {
        // Distinguish combinations even when the document canonical consolidates query variants.
        if (filters.Length == 0) return SiteUrl + (view == "ultimate" ? "/leaderboard" : "/league/" + view);
        if (view == "ultimate" && filters.Length == 1)
        {
            var league = LeagueFilters.FirstOrDefault(pair => pair.Key != "professional" && pair.Value.Equals(filters[0], StringComparison.OrdinalIgnoreCase));
            if (league.Key is not null) return SiteUrl + "/league/" + league.Key;
            if (!filters[0].Equals("Professional", StringComparison.OrdinalIgnoreCase) && FlagRouteCatalog.TryCreate(filters[0], out var flag))
                return SiteUrl + flag.Path;
        }
        return SiteUrl + "/leaderboard?filters=" +
               Uri.EscapeDataString(string.Join(',', filters.Select(f => f.ToLowerInvariant()).Order(StringComparer.Ordinal))) +
               (view == "ultimate" ? "" : "&view=" + view);
    }

    private static string DecodeFilter(string value) => Uri.UnescapeDataString(Uri.UnescapeDataString(value)).Trim();
    private static string RouteSlug(string slug) => slug.Replace('_', '-').ToLowerInvariant();
    private static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    private static string DisplayName(JsonObject obj) => string.IsNullOrWhiteSpace(Text(obj, "DisplayName")) ? Text(obj, "Name") : Text(obj, "DisplayName");
    private static Dictionary<string, object> Reference(string id) => new() { ["@id"] = id };
}
