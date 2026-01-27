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
    private readonly DatabaseManager _db;

    private const int SeasonIdConst = 2025;
    private static readonly DateTime SeasonClosesAtUtcConst = new(2026, 1, 16, 7, 41, 50, DateTimeKind.Utc);
    private const string ClockIdConst = "PhenoAge";

    public SeasonFinalizerService(AthleteDataService athletes, EventDataService events, DatabaseManager db)
    {
        _athletes = athletes ?? throw new ArgumentNullException(nameof(athletes));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _db = db ?? throw new ArgumentNullException(nameof(db));

        EnsureSeasonFinalResultsTable();
    }

    public SeasonFinalizationResult TryFinalizeActiveSeason(DateTime nowUtc)
    {
        nowUtc = EnsureUtc(nowUtc);

        if (nowUtc < SeasonClosesAtUtcConst)
            return new SeasonFinalizationResult(false, false, SeasonIdConst, SeasonClosesAtUtcConst, ClockIdConst, 0);

        var hasEventFinalResults = _events.HasAnySeasonFinalResults(seasonId: SeasonIdConst);
        var hasDbFinalResults = HasAnySeasonFinalResultsInDb(seasonId: SeasonIdConst, clockId: ClockIdConst);
        var titleRaw = BuildSeasonConcludedTitle();
        var hasCustomEvent = _events.HasCustomEventWithTitle(titleRaw);

        var alreadyFinalized = hasEventFinalResults;
        int computedCount = 0;

        if (!hasEventFinalResults || !hasDbFinalResults || !hasCustomEvent)
        {
            var order = _athletes.GetRankingsOrder(SeasonClosesAtUtcConst);
            if (order.Count == 0)
                return new SeasonFinalizationResult(true, alreadyFinalized, SeasonIdConst, SeasonClosesAtUtcConst, ClockIdConst, 0);

            var rows = BuildRows(order);
            computedCount = rows.Count;

            if (rows.Count > 0)
            {
                // Season closing actions:
                if (!hasEventFinalResults)
                    _events.UpsertSeasonFinalResults(seasonId: SeasonIdConst, closesAtUtc: SeasonClosesAtUtcConst, clockId: ClockIdConst, rows: rows);
                if (!hasDbFinalResults)
                    PersistSeasonFinalResults(SeasonClosesAtUtcConst, rows);
                if (!hasCustomEvent)
                    CreateSeasonConcludedCustomEvent(nowUtc, order, rows);
            }
        }

        return new SeasonFinalizationResult(true, alreadyFinalized, SeasonIdConst, SeasonClosesAtUtcConst, ClockIdConst, computedCount);
    }

    private void EnsureSeasonFinalResultsTable()
    {
        _db.Run(sqlite =>
        {
            using (var cmd = sqlite.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS SeasonFinalResults(
    SeasonId INTEGER NOT NULL,
    ClockId TEXT NOT NULL,
    AthleteSlug TEXT NOT NULL,
    Place INTEGER NOT NULL,
    AgeDiff REAL NOT NULL,
    SeasonClosesAtUtc TEXT NOT NULL,
    FinalizedAtUtc TEXT NOT NULL,
    PRIMARY KEY (SeasonId, ClockId, AthleteSlug)
);";
                cmd.ExecuteNonQuery();
            }

            using (var cmd = sqlite.CreateCommand())
            {
                cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_SeasonFinalResults_Season_Clock_Place ON SeasonFinalResults(SeasonId, ClockId, Place);";
                cmd.ExecuteNonQuery();
            }
        });
    }

    private bool HasAnySeasonFinalResultsInDb(int seasonId, string clockId)
    {
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM SeasonFinalResults WHERE SeasonId = $seasonId AND ClockId = $clockId LIMIT 1;";
            cmd.Parameters.AddWithValue("$seasonId", seasonId);
            cmd.Parameters.AddWithValue("$clockId", clockId);
            return cmd.ExecuteScalar() is not null;
        });
    }

    private void PersistSeasonFinalResults(DateTime nowUtc, List<SeasonFinalResultRow> rows)
    {
        var closesAtUtc = SeasonClosesAtUtcConst.ToString("O", CultureInfo.InvariantCulture);
        var finalizedAtUtc = nowUtc.ToString("O", CultureInfo.InvariantCulture);

        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            using var cmd = sqlite.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR IGNORE INTO SeasonFinalResults (SeasonId, ClockId, AthleteSlug, Place, AgeDiff, SeasonClosesAtUtc, FinalizedAtUtc) VALUES ($seasonId, $clockId, $athleteSlug, $place, $ageDiff, $seasonClosesAtUtc, $finalizedAtUtc);";

            var pSeasonId = cmd.Parameters.AddWithValue("$seasonId", 0);
            var pClockId = cmd.Parameters.AddWithValue("$clockId", "");
            var pAthleteSlug = cmd.Parameters.AddWithValue("$athleteSlug", "");
            var pPlace = cmd.Parameters.AddWithValue("$place", 0);
            var pAgeDiff = cmd.Parameters.AddWithValue("$ageDiff", 0d);
            var pSeasonClosesAtUtc = cmd.Parameters.AddWithValue("$seasonClosesAtUtc", closesAtUtc);
            var pFinalizedAtUtc = cmd.Parameters.AddWithValue("$finalizedAtUtc", finalizedAtUtc);

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                pSeasonId.Value = r.SeasonId;
                pClockId.Value = r.ClockId;
                pAthleteSlug.Value = r.AthleteSlug;
                pPlace.Value = r.Place;
                pAgeDiff.Value = r.AgeDiff;

                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        });
    }

    private void CreateSeasonConcludedCustomEvent(DateTime nowUtc, JsonArray order, List<SeasonFinalResultRow> rows)
    {
        var slugToName = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] is not JsonObject o) continue;

            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;
            if (slugToName.ContainsKey(slug)) continue;

            var name =
                o["DisplayName"]?.GetValue<string>() ??
                o["Name"]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(name))
                slugToName[slug] = name;
        }

        var seasonTwoDigits = (SeasonIdConst % 100).ToString("00", CultureInfo.InvariantCulture);

        var top = rows
            .OrderBy(x => x.Place)
            .Take(3)
            .ToList();

        var titleRaw = BuildSeasonConcludedTitle();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"LWC{seasonTwoDigits} has officially concluded. Thank you to all participants and contributors who engaged with the competition throughout the year.");
        sb.AppendLine();
        sb.AppendLine("[strong](Top 3 Final Rankings:)");
        sb.AppendLine();

        for (int i = 0; i < top.Count; i++)
        {
            var r = top[i];

            var name = r.AthleteSlug;
            if (slugToName.TryGetValue(r.AthleteSlug, out var n) && !string.IsNullOrWhiteSpace(n))
                name = n;

            var years = r.AgeDiff;
            if (double.IsNaN(years) || double.IsInfinity(years)) years = 0;
            years = Math.Abs(years);

            var yearsReduced = Math.Round(years, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture);

            sb.AppendLine($"{r.Place}. [bold]({name}) — {yearsReduced} years reduced");
        }

        sb.AppendLine();
        sb.AppendLine($"Final rankings and achievements are now locked for the {SeasonIdConst} season. The leaderboard reflects the complete results from this year's competition. Ceremony, prizes, and related activities for the {SeasonIdConst} competition are still to be completed.");
        sb.AppendLine();
        sb.AppendLine($"We're now also preparing for the {SeasonIdConst + 1} season. Details about registration and any rule updates will be announced when ready.");

        var contentRaw = sb.ToString().TrimEnd();

        _events.CreateCustomEvent(titleRaw: titleRaw, contentRaw: contentRaw, occurredAtUtc: nowUtc, relevance: 15d);
    }

    private static string BuildSeasonConcludedTitle() => $"{SeasonIdConst} Season Concluded";

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
