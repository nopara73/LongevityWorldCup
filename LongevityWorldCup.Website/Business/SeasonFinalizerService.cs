using System.Globalization;
using System.Text.Json.Nodes;

namespace LongevityWorldCup.Website.Business;

public sealed record SeasonFinalizationResult(
    bool IsDue,
    bool AlreadyFinalized,
    int SeasonId,
    DateTime SeasonClosesAtUtc,
    string ClockId,
    int ComputedResultsCount);

public sealed record SeasonFinalResultRow(
    string AthleteSlug,
    int SeasonId,
    int Place,
    string ClockId,
    double AgeDiff);

public sealed class SeasonFinalizerService
{
    private readonly AthleteDataService _athletes;
    private readonly EventDataService _events;

    private const int SeasonIdConst = 2025;
    private static readonly DateTime SeasonClosesAtUtcConst = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string ClockIdConst = "PhenoAge";

    public SeasonFinalizerService(AthleteDataService athletes, EventDataService events)
    {
        _athletes = athletes ?? throw new ArgumentNullException(nameof(athletes));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public SeasonFinalizationResult TryFinalizeActiveSeason(DateTime nowUtc)
    {
        nowUtc = EnsureUtc(nowUtc);

        if (nowUtc < SeasonClosesAtUtcConst)
            return new SeasonFinalizationResult(false, false, SeasonIdConst, SeasonClosesAtUtcConst, ClockIdConst, 0);

        if (_events.HasAnySeasonFinalResults(seasonId: SeasonIdConst))
            return new SeasonFinalizationResult(true, true, SeasonIdConst, SeasonClosesAtUtcConst, ClockIdConst, 0);

        var order = _athletes.GetRankingsOrder(SeasonClosesAtUtcConst);
        if (order.Count == 0)
            return new SeasonFinalizationResult(true, false, SeasonIdConst, SeasonClosesAtUtcConst, ClockIdConst, 0);

        var rows = BuildRows(order);

        _events.UpsertSeasonFinalResults(
            seasonId: SeasonIdConst,
            closesAtUtc: SeasonClosesAtUtcConst,
            clockId: ClockIdConst,
            rows: rows);

        return new SeasonFinalizationResult(true, false, SeasonIdConst, SeasonClosesAtUtcConst, ClockIdConst, rows.Count);
    }

    private static List<SeasonFinalResultRow> BuildRows(JsonArray order)
    {
        var rows = new List<SeasonFinalResultRow>(order.Count);

        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] is not JsonObject o) continue;

            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;

            var place = i + 1;

            double ageDiff = 0;
            if (o["AgeDifference"] is JsonValue jv && jv.TryGetValue<double>(out var d) && !double.IsNaN(d) && !double.IsInfinity(d))
                ageDiff = d;

            rows.Add(new SeasonFinalResultRow(
                AthleteSlug: slug,
                SeasonId: SeasonIdConst,
                Place: place,
                ClockId: ClockIdConst,
                AgeDiff: ageDiff));
        }

        return rows;
    }

    private static DateTime EnsureUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
}
