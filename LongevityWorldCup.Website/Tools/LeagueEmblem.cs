using System.Text.Json;

namespace LongevityWorldCup.Website.Tools;

internal static class LeagueEmblem
{
    public const string Placeholder = """<span class="league-emblem" aria-hidden="true" hidden></span>""";
    private static readonly Dictionary<string, string> Glyphs = LoadGlyphs();

    private static Dictionary<string, string> LoadGlyphs()
    {
        using var stream = typeof(LeagueEmblem).Assembly.GetManifestResourceStream("league-icon-codes.json")
            ?? throw new InvalidOperationException("Missing league icon catalog.");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!
            .Values.SelectMany(group => group).ToDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public static string[] Classes(string[] tracks, IEnumerable<string> divisions, IEnumerable<string> generations,
        IEnumerable<string> exclusive, IEnumerable<string> flags)
    {
        // Same group order as the filter panel. The clock changes ranking, not league membership.
        var leagues = (tracks.Length == 1 ? tracks : Array.Empty<string>())
            .Concat(divisions.Order(StringComparer.Ordinal)).Concat(generations.Order(StringComparer.Ordinal))
            .Concat(exclusive.Order(StringComparer.Ordinal));
        return leagues.Select(name => "fas " + Glyphs.GetValueOrDefault(name, "fa-trophy") + " filter-icon")
            .Concat(flags.Order(StringComparer.Ordinal).Select(name => FlagIconCatalog.GetCode(name) is { } code
                ? $"fi fi-{code} lwc-flag-icon" : "fas fa-flag filter-icon"))
            .Take(2).ToArray();
    }

    public static string Render(IReadOnlyList<string> classes)
    {
        var marks = classes.Select((value, index) =>
        {
            var tag = value.StartsWith("fi ", StringComparison.Ordinal) ? "span" : "i";
            var role = index == 0 ? "league-emblem-mark" : "league-emblem-badge";
            return $"""<{tag} class="{value} {role}" aria-hidden="true"></{tag}>""";
        });
        return $"""<span class="league-emblem" aria-hidden="true">{string.Concat(marks)}</span>""";
    }
}
