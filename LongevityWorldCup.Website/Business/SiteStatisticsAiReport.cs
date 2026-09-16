using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

public sealed partial class SiteStatisticsService
{
    private static SiteStatisticsAiReport ReadAiReferrals(SqliteConnection sqlite, DateTimeOffset from,
        DateTimeOffset to, SiteStatisticsDashboardQuery query)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = $$"""
            SELECT {{EffectiveAiProviderSql}}, s.LandingRoute, {{EffectiveReferrerSql}}, s.FirstUtmSource,
                   COUNT(*),
                   SUM(CASE WHEN e.EventName IN ({{PageViewEventNamesSql}}) THEN 1 ELSE 0 END),
                   MAX(CASE WHEN e.EventName = 'calculator_used' THEN 1 ELSE 0 END),
                   MAX(CASE WHEN e.EventName = 'calculator_result_generated' THEN 1 ELSE 0 END),
                   MAX(CASE WHEN e.EventName = 'application_started' THEN 1 ELSE 0 END),
                   MAX(CASE WHEN e.EventName = 'application_submit_succeeded'
                             AND json_extract(CASE WHEN json_valid(e.MetadataJson) THEN e.MetadataJson ELSE '{}' END, '$.submissionKind') = 'full-application' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN e.EventName = 'application_submit_succeeded'
                             AND json_extract(CASE WHEN json_valid(e.MetadataJson) THEN e.MetadataJson ELSE '{}' END, '$.submissionKind') = 'full-application' THEN 1 ELSE 0 END),
                   MAX(CASE WHEN e.EventName = 'application_submit_succeeded'
                             AND coalesce(json_extract(CASE WHEN json_valid(e.MetadataJson) THEN e.MetadataJson ELSE '{}' END, '$.submissionKind'), 'unknown') = 'unknown' THEN 1 ELSE 0 END)
            FROM SiteStatisticEvents e
            LEFT JOIN SiteStatisticSessions s ON s.SessionHash = e.SessionHash
            WHERE e.OccurredAtUtc >= @from AND e.OccurredAtUtc < @to
              AND (@flow = '' OR e.Flow = @flow)
              AND (@device = '' OR e.DeviceClass = @device)
              AND {{SourceFilterSql}}
              AND {{EffectiveAiProviderSql}} IS NOT NULL
            GROUP BY e.SessionHash;
            """;
        AddTrafficFilterParameters(cmd, from, to, query);
        var sessions = new List<AiSession>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var attribution = AiReferralPolicy.Classify(ReadNullableString(reader, 2), ReadNullableString(reader, 3));
                sessions.Add(new AiSession(ReadString(reader, 0),
                    BuildRouteProjection(ReadNullableString(reader, 1))?.Route?.Split('?')[0] ?? "unknown",
                    attribution?.Basis ?? "referrer", reader.GetInt64(4), reader.GetInt64(5),
                    reader.GetInt64(6) > 0, reader.GetInt64(7) > 0, reader.GetInt64(8) > 0,
                    reader.GetInt64(9) > 0, reader.GetInt64(10), reader.GetInt64(11) > 0));
            }
        }

        using var coverage = sqlite.CreateCommand();
        coverage.CommandText = "SELECT EnabledAtUtc FROM SiteStatisticFeatures WHERE Name = 'ai-funnel-v1';";
        var since = Convert.ToString(coverage.ExecuteScalar(), CultureInfo.InvariantCulture)!;
        return new SiteStatisticsAiReport(since, AiReferralPolicy.Providers.Select(p => new SiteStatisticsAiProvider(p.Id, p.Name)).ToArray(),
            SummarizeAiSessions("all", "All identifiable AI", sessions),
            sessions.GroupBy(s => s.Provider).Select(g => SummarizeAiSessions(g.Key,
                AiReferralPolicy.Providers.Single(p => p.Id == g.Key).Name, g.ToList())).OrderByDescending(r => r.Visits).ThenBy(r => r.Label).ToArray(),
            sessions.GroupBy(s => s.LandingRoute).Select(g => SummarizeAiSessions(g.Key, g.Key, g.ToList()))
                .OrderByDescending(r => r.Visits).ThenBy(r => r.Label).ToArray());
    }

    private static SiteStatisticsAiMetrics SummarizeAiSessions(string key, string label, IReadOnlyList<AiSession> sessions)
    {
        var starts = sessions.Count(s => s.Started);
        var applications = sessions.Count(s => s.Applied);
        return new(key, label, sessions.Count, sessions.Sum(s => s.Events), sessions.Sum(s => s.PageViews),
            sessions.Count(s => s.Basis == "referrer"), sessions.Count(s => s.Basis == "campaign"),
            sessions.Count(s => s.Used), sessions.Count(s => s.Result), starts, applications,
            sessions.Sum(s => s.ApplicationEvents), sessions.Count(s => s.UnknownApplication),
            sessions.Count == 0 ? null : applications / (double)sessions.Count,
            starts == 0 ? null : sessions.Count(s => s.Started && s.Applied) / (double)starts);
    }

    private sealed record AiSession(string Provider, string LandingRoute, string Basis, long Events, long PageViews,
        bool Used, bool Result, bool Started, bool Applied, long ApplicationEvents, bool UnknownApplication);
}

public sealed record SiteStatisticsAiProvider(string Id, string Name);
public sealed record SiteStatisticsAiReport(string InteractionTrackingSinceUtc, IReadOnlyList<SiteStatisticsAiProvider> ProviderOptions,
    SiteStatisticsAiMetrics Totals, IReadOnlyList<SiteStatisticsAiMetrics> Providers, IReadOnlyList<SiteStatisticsAiMetrics> LandingPages);

public sealed record SiteStatisticsAiMetrics(string Key, string Label, long Visits, long Events, long PageViews,
    long ReferrerVisits, long CampaignVisits, long CalculatorUses, long CalculatorResults, long ApplicationStarts,
    long Applications, long ApplicationEvents, long UnknownApplicationSessions, double? VisitToApplicationRate,
    double? StartToApplicationRate);
