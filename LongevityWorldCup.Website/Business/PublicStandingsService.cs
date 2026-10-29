using System.Text.Json;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

/// <summary>The public Ultimate League preview, derived once per dataset and UTC day.</summary>
public sealed class PublicStandingsService(IAthleteSnapshotProvider athletes, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private string? _sourceKey;
    private PublicStandings? _cached;

    public PublicStandings Get()
    {
        lock (_gate)
        {
            var source = athletes.GetAthletesSnapshot();
            var day = _clock.GetUtcNow().UtcDateTime.Date;
            var key = day.ToString("O") + PublicGetCacheHeaders.BuildWeakContentETag(source.ToJsonString());
            if (key == _sourceKey) return _cached!;

            var board = new PublicLeaderboardSnapshot(source, day);
            var bySlug = board.Athletes.ToDictionary(a => a.Row.Slug);
            // Select precision across the entire league before taking the preview:
            // eighth and ninth can round to the same displayed score.
            var rows = board.Select("/leaderboard", QueryCollection.Empty).Snapshot.Rows.Take(8)
                .Select(row => new PublicStanding(row.Rank, row.DisplayName, row.AthletePath,
                    row.LeaderboardThumbnailUrl, row.EffectiveAgeReductionYears, row.MetricDecimals,
                    LastMonthPlacement(bySlug[row.Slug].Source))).ToArray();
            _cached = new PublicStandings(Array.AsReadOnly(rows),
                PublicGetCacheHeaders.BuildWeakContentETag(JsonSerializer.Serialize(rows)));
            _sourceKey = key;
            return _cached;
        }
    }

    // Placements are [yesterday, last week, last month, last year]. Unknown is not zero.
    private static int? LastMonthPlacement(JsonObject athlete)
    {
        var node = athlete["Placements"];
        if (node is JsonValue text && text.TryGetValue<string>(out var raw))
        {
            try { node = JsonNode.Parse(raw); }
            catch (JsonException) { return null; }
        }
        return node is JsonArray placements && placements.Count > 2
            && placements[2] is JsonValue month && month.TryGetValue<int>(out var rank) && rank > 0
                ? rank : null;
    }
}

public sealed record PublicStanding(int Rank, string Name, string Path, string? Thumb,
    double? AgeReduction, int Decimals, int? LastMonthRank);
public sealed record PublicStandings(IReadOnlyList<PublicStanding> Rows, string ETag);
