using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Globalization;
using Microsoft.Data.Sqlite;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

/// <summary>
/// Computes current badge holders and persists a snapshot into SQLite.
/// Human-friendly form: one row per athlete-badge in table BadgeAwards,
/// with BadgeLabel + LeagueCategory/LeagueValue text fields.
/// No event emission, no frontend changes. Run at service startup.
/// </summary>
public sealed class BadgeDataService : IDisposable
{
    private readonly AthleteDataService _athletes;
    private readonly DatabaseManager _db;
    private readonly DateTime _asOfUtc;
    private readonly EventDataService _events; // NEW: event sink for BadgeAward events via DI

    private const string AwardsTable = "BadgeAwards";
    private readonly SemaphoreSlim _athletesChangedRecomputeGate = new SemaphoreSlim(1, 1);
    private int _athletesChangedRecomputeAgain;

    public BadgeDataService(AthleteDataService athletes, EventDataService events, DatabaseManager db)
    {
        _athletes = athletes ?? throw new ArgumentNullException(nameof(athletes));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _asOfUtc = DateTime.UtcNow.Date;

        EnsureTables();
        ComputeAndPersistAwards();
        _athletes.RefreshBadgesFromDatabase();

        _athletes.AthletesChanged += OnAthletesChanged;
    }

    private void OnAthletesChanged()
    {
        if (!_athletesChangedRecomputeGate.Wait(0))
        {
            Interlocked.Exchange(ref _athletesChangedRecomputeAgain, 1);
            return;
        }

        try
        {
            while (true)
            {
                Interlocked.Exchange(ref _athletesChangedRecomputeAgain, 0);

                // Recompute awards based on the latest athlete data,
                // then hydrate the latest awards back into athlete JSONs.
                ComputeAndPersistAwards();
                _athletes.RefreshBadgesFromDatabase();

                if (Interlocked.CompareExchange(ref _athletesChangedRecomputeAgain, 0, 1) == 0)
                    break;
            }
        }
        finally
        {
            _athletesChangedRecomputeGate.Release();
        }
    }

    /// <summary>
    /// Ensure human-friendly awards table exists.
    /// </summary>
    private void EnsureTables()
    {
        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $@"
CREATE TABLE IF NOT EXISTS {AwardsTable} (
    BadgeLabel     TEXT NOT NULL,
    LeagueCategory TEXT NOT NULL,
    LeagueValue    TEXT NULL,
    Place          INTEGER NULL,
    AthleteSlug    TEXT NOT NULL,
    DefinitionHash TEXT NULL,
    UpdatedAt      TEXT NOT NULL,
    PRIMARY KEY (BadgeLabel, LeagueCategory, LeagueValue, Place, AthleteSlug)
);";
            cmd.ExecuteNonQuery();
        });
    }

    /// <summary>
    /// Compute the snapshot and replace BadgeAwards in a single transaction.
    /// </summary>
    public void ComputeAndPersistAwards()
    {
        var stats = BuildAthleteStats(_asOfUtc);
        HydrateComputedStatsIntoAthletes(stats);
        var defs = BuildBadgeDefinitions();

        var awards = new List<AwardRow>(capacity: 4096);

        foreach (var def in defs)
        {
            if (def.Scope == BadgeScope.Global)
            {
                var ranked = SelectTop(stats.Values, def);
                var (cat, val) = ("Global", (string?)null);
                var ruleHash = BuildRankedRuleHash(def.Label, cat, def.MetricKey, def.TopN, def.SortDirection, tieBreakKey: "dob_then_name", generationBandsVersion: null);

                for (int i = 0; i < ranked.Count; i++)
                {
                    awards.Add(new AwardRow
                    {
                        BadgeLabel = def.Label,
                        LeagueCategory = cat,
                        LeagueValue = val,
                        Place = i + 1,
                        AthleteSlug = ranked[i].Slug,
                        DefinitionHash = ruleHash
                    });
                }
            }
            else
            {
                foreach (var (cat, val, group) in GroupByLeague(stats.Values, def.Scope))
                {
                    var ranked = SelectTop(group, def);
                    if (ranked.Count == 0) continue;

                    var genVersion = cat == "Generation" ? "gen-v1" : null;
                    var ruleHash = BuildRankedRuleHash(def.Label, cat, def.MetricKey, def.TopN, def.SortDirection, tieBreakKey: "dob_then_name", generationBandsVersion: genVersion);

                    for (int i = 0; i < ranked.Count; i++)
                    {
                        awards.Add(new AwardRow
                        {
                            BadgeLabel = def.Label,
                            LeagueCategory = cat,
                            LeagueValue = val,
                            Place = i + 1,
                            AthleteSlug = ranked[i].Slug,
                            DefinitionHash = ruleHash
                        });
                    }
                }
            }
        }

        AddDomainAwards(stats, awards);
        AddSubmissionAwards(stats, awards);
        AddPhenoDiffAwards(stats, awards);
        AddCrowdAwards(stats, awards);
        AddEditorialAwards(stats, awards);

        var previous = ReadCurrentAwardsSnapshot();
        EmitBadgeAwardEvents(previous, awards, DateTime.UtcNow);

        var now = DateTime.UtcNow.ToString("o");

        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            using (var del = sqlite.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {AwardsTable};";
                del.ExecuteNonQuery();
            }

            using (var ins = sqlite.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = $@"
INSERT INTO {AwardsTable} (BadgeLabel, LeagueCategory, LeagueValue, Place, AthleteSlug, DefinitionHash, UpdatedAt)
VALUES (@bl, @lc, @lv, @p, @a, @dh, @u);";
                var pBL = ins.Parameters.Add("@bl", SqliteType.Text);
                var pLC = ins.Parameters.Add("@lc", SqliteType.Text);
                var pLV = ins.Parameters.Add("@lv", SqliteType.Text);
                var pP = ins.Parameters.Add("@p", SqliteType.Integer);
                var pA = ins.Parameters.Add("@a", SqliteType.Text);
                var pD = ins.Parameters.Add("@dh", SqliteType.Text);
                var pU = ins.Parameters.Add("@u", SqliteType.Text);

                foreach (var r in awards)
                {
                    pBL.Value = r.BadgeLabel;
                    pLC.Value = r.LeagueCategory;
                    pLV.Value = r.LeagueValue is null ? (object)DBNull.Value : r.LeagueValue;
                    pP.Value = r.Place.HasValue ? r.Place.Value : (object)DBNull.Value;
                    pA.Value = r.AthleteSlug;
                    pD.Value = r.DefinitionHash ?? (object)DBNull.Value;
                    pU.Value = now;
                    ins.ExecuteNonQuery();
                }
            }

            tx.Commit();
        });
    }

    private void HydrateComputedStatsIntoAthletes(Dictionary<string, AthleteStats> stats)
    {
        _athletes.UpdateAthletesJsonInPlace(o =>
        {
            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) return;
            if (!stats.TryGetValue(slug, out var s)) return;

            if (s.ChronoAge.HasValue) o["ChronoAge"] = s.ChronoAge.Value;
            else o.Remove("ChronoAge");
            if (s.LowestPhenoAge.HasValue) o["LowestPhenoAge"] = s.LowestPhenoAge.Value;
            else o.Remove("LowestPhenoAge");
            o["SubmissionCount"] = s.SubmissionCount;

            if (s.BestMarkerValues is not null)
            {
                o["BestMarkerValues"] = new JsonArray(s.BestMarkerValues.Select(v => (JsonNode)v).ToArray());
            }
            else
            {
                o.Remove("BestMarkerValues");
            }

            if (s.PhenoAgeDiffFromBaseline.HasValue) o["PhenoAgeDiffFromBaseline"] = s.PhenoAgeDiffFromBaseline.Value;
            else o.Remove("PhenoAgeDiffFromBaseline");
        });
    }

    private List<AwardRow> ReadCurrentAwardsSnapshot()
    {
        var list = new List<AwardRow>(1024);
        try
        {
            _db.Run(sqlite =>
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText = $"SELECT BadgeLabel, LeagueCategory, LeagueValue, Place, AthleteSlug, DefinitionHash FROM {AwardsTable};";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new AwardRow
                    {
                        BadgeLabel = r.GetString(0),
                        LeagueCategory = r.GetString(1),
                        LeagueValue = r.IsDBNull(2) ? null : r.GetString(2),
                        Place = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                        AthleteSlug = r.GetString(4),
                        DefinitionHash = r.IsDBNull(5) ? null : r.GetString(5)
                    });
                }
            });
        }
        catch
        {
        }

        return list;
    }

    private void EmitBadgeAwardEvents(
        IReadOnlyList<AwardRow> before,
        IReadOnlyList<AwardRow> after,
        DateTime occurredAtUtc)
    {
        static string SlotKey(string label, string cat, string? val, int? place)
            => $"{label}|{cat}|{val ?? ""}|{(place.HasValue ? place.Value.ToString(CultureInfo.InvariantCulture) : "")}";

        static string AbKey(string athleteSlug, string label, string cat, string? val)
            => $"{athleteSlug}|{label}|{cat}|{val ?? ""}";

        var beforeMap = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var x in before)
        {
            var k = SlotKey(x.BadgeLabel, x.LeagueCategory, x.LeagueValue, x.Place);
            if (!beforeMap.TryGetValue(k, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                beforeMap[k] = set;
            }

            set.Add(x.AthleteSlug);
        }

        var afterMap = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var x in after)
        {
            var k = SlotKey(x.BadgeLabel, x.LeagueCategory, x.LeagueValue, x.Place);
            if (!afterMap.TryGetValue(k, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                afterMap[k] = set;
            }

            set.Add(x.AthleteSlug);
        }

        var prevBest = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var x in before)
        {
            if (!x.Place.HasValue) continue;

            var k = AbKey(x.AthleteSlug, x.BadgeLabel, x.LeagueCategory, x.LeagueValue);
            if (!prevBest.TryGetValue(k, out var best) || x.Place.Value < best)
                prevBest[k] = x.Place.Value;
        }

        var items = new List<BadgeEventItem>();

        foreach (var kv in afterMap)
        {
            var key = kv.Key;
            var nextSet = kv.Value;
            beforeMap.TryGetValue(key, out var prevSet);
            prevSet ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var parts = key.Split('|');
            var label = parts.Length > 0 ? parts[0] : "";
            var cat = parts.Length > 1 ? parts[1] : "Global";
            var valStr = parts.Length > 2 ? parts[2] : null;

            int? place = null;
            if (parts.Length > 3 && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var placeParsed))
                place = placeParsed;

            var adds = nextSet.Except(prevSet, StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList();
            if (adds.Count == 0) continue;

            var removes = prevSet.Except(nextSet, StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList();

            if (place.HasValue)
            {
                var addsForward = new List<string>();
                foreach (var slug in adds)
                {
                    var abKey = AbKey(slug, label, cat, string.IsNullOrEmpty(valStr) ? null : valStr);
                    var forward = !prevBest.TryGetValue(abKey, out var prevPlace) || place.Value < prevPlace;
                    if (forward) addsForward.Add(slug);
                }

                // Only 1-to-1 swap sets ReplacedSlug; otherwise null
                var singleSwap = prevSet.Count == 1 && nextSet.Count == 1 && addsForward.Count == 1 && removes.Count == 1 && !string.Equals(addsForward.First(), removes.First(), StringComparison.OrdinalIgnoreCase);

                var replacedSlugs = (!singleSwap && nextSet.Count == 1 && addsForward.Count == 1 && removes.Count > 1)
                    ? removes
                    : null;

                for (int i = 0; i < addsForward.Count; i++)
                {
                    var replaced = singleSwap ? removes[0] : null;
                    items.Add(new BadgeEventItem
                    {
                        AthleteSlug = addsForward[i],
                        OccurredAtUtc = occurredAtUtc,
                        BadgeLabel = label,
                        LeagueCategory = cat,
                        LeagueValue = string.IsNullOrEmpty(valStr) ? null : valStr,
                        Place = place,
                        ReplacedSlug = replaced,
                        ReplacedSlugs = replacedSlugs
                    });
                }
            }
            else
            {
                // Only 1-to-1 swap sets ReplacedSlug; otherwise null
                var singleSwap = prevSet.Count == 1 && nextSet.Count == 1 && adds.Count == 1 && removes.Count == 1 && !string.Equals(adds.First(), removes.First(), StringComparison.OrdinalIgnoreCase);

                var replacedSlugs = (!singleSwap && nextSet.Count == 1 && adds.Count == 1 && removes.Count > 1)
                    ? removes
                    : null;

                for (int i = 0; i < adds.Count; i++)
                {
                    var replaced = singleSwap ? removes[0] : null;
                    items.Add(new BadgeEventItem
                    {
                        AthleteSlug = adds[i],
                        OccurredAtUtc = occurredAtUtc,
                        BadgeLabel = label,
                        LeagueCategory = cat,
                        LeagueValue = string.IsNullOrEmpty(valStr) ? null : valStr,
                        Place = null,
                        ReplacedSlug = replaced,
                        ReplacedSlugs = replacedSlugs
                    });
                }
            }
        }

        if (items.Count > 0)
            _events.CreateBadgeAwardEvents(
                items.Select(i => (i.AthleteSlug, i.OccurredAtUtc, i.BadgeLabel, i.LeagueCategory, i.LeagueValue, i.Place, i.ReplacedSlug)),
                skipIfExists: true);
    }


    private static string? TryGetStringNode(JsonObject o, string key)
    {
        try
        {
            if (o.TryGetPropertyValue(key, out JsonNode? node) && node is not null)
                return node.GetValue<string?>();
        }
        catch
        {
        }

        return null;
    }

    private static string BuildEditorialRuleHash(string label, string? note = null)
    {
        string sig = $"label={label}|category=Global|type=editorial|note={note ?? "n/a"}";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sig));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private void AddEditorialAwards(
        Dictionary<string, AthleteStats> stats,
        List<AwardRow> awards)
    {
        var nameToSlug = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var slugToObj = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in _athletes.Athletes)
        {
            if (node is not JsonObject o) continue;

            var slug =
                TryGetStringNode(o, "AthleteSlug") ??
                TryGetStringNode(o, "athleteSlug") ??
                TryGetStringNode(o, "Slug") ??
                TryGetStringNode(o, "slug");

            if (string.IsNullOrWhiteSpace(slug)) continue;

            var name =
                TryGetStringNode(o, "Name") ??
                TryGetStringNode(o, "name") ??
                slug;

            nameToSlug[name] = slug;
            slugToObj[slug] = o;
        }

        foreach (var (name, slug) in nameToSlug)
        {
            if (!slugToObj.TryGetValue(slug, out var obj)) continue;
            var link = TryGetStringNode(obj, "PodcastLink") ?? TryGetStringNode(obj, "podcastLink");
            if (!string.IsNullOrWhiteSpace(link))
            {
                awards.Add(new AwardRow
                {
                    BadgeLabel = "Podcast",
                    LeagueCategory = "Global",
                    LeagueValue = null,
                    Place = null,
                    AthleteSlug = slug,
                    DefinitionHash = BuildEditorialRuleHash("Podcast", "has_podcast_link")
                });
            }
        }

        var firstApplicants = new (string Name, int Place)[]
        {
            ("Alan V", 1), ("Cody Hergenroeder", 2), ("Spiderius", 3), ("Jesse", 4), ("Tone Vays", 5),
            ("Stellar Madic", 6), ("RichLee", 7), ("ScottBrylow", 8), ("Mind4u2cn", 9), ("Dave Pascoe", 10),
        };
        foreach (var (name, place) in firstApplicants)
        {
            if (!nameToSlug.TryGetValue(name, out var slug)) continue;
            awards.Add(new AwardRow
            {
                BadgeLabel = "First Applicants",
                LeagueCategory = "Global",
                LeagueValue = null,
                Place = place,
                AthleteSlug = slug,
                DefinitionHash = BuildEditorialRuleHash("First Applicants", $"rank={place}")
            });
        }

        var pregnancyNames = new[] { "Olga Vresca" };
        foreach (var name in pregnancyNames)
        {
            if (!nameToSlug.TryGetValue(name, out var slug)) continue;
            awards.Add(new AwardRow
            {
                BadgeLabel = "Pregnancy",
                LeagueCategory = "Global",
                LeagueValue = null,
                Place = null,
                AthleteSlug = slug,
                DefinitionHash = BuildEditorialRuleHash("Pregnancy", "legacy_flag")
            });
        }

        if (nameToSlug.TryGetValue("nopara73", out var hostSlug))
        {
            awards.Add(new AwardRow
            {
                BadgeLabel = "Host",
                LeagueCategory = "Global",
                LeagueValue = null,
                Place = null,
                AthleteSlug = hostSlug,
                DefinitionHash = BuildEditorialRuleHash("Host", "organizer")
            });
        }

        if (nameToSlug.TryGetValue("Cornee", out var paSlug))
        {
            awards.Add(new AwardRow
            {
                BadgeLabel = "Perfect Application",
                LeagueCategory = "Global",
                LeagueValue = null,
                Place = null,
                AthleteSlug = paSlug,
                DefinitionHash = BuildEditorialRuleHash("Perfect Application", "legacy_flag")
            });
        }
    }

    private sealed class AwardRow
    {
        public required string BadgeLabel { get; init; }
        public required string LeagueCategory { get; init; }
        public string? LeagueValue { get; init; }
        public int? Place { get; init; }
        public required string AthleteSlug { get; init; }
        public string? DefinitionHash { get; init; }
    }

    private sealed class BadgeEventItem
    {
        public required string AthleteSlug { get; init; }
        public required DateTime OccurredAtUtc { get; init; }
        public required string BadgeLabel { get; init; }
        public required string LeagueCategory { get; init; }
        public string? LeagueValue { get; init; }
        public int? Place { get; init; }
        public string? ReplacedSlug { get; init; }
        public IReadOnlyList<string>? ReplacedSlugs { get; init; }
    }

    private static void AddRange(List<AwardRow> sink, IEnumerable<AwardRow> rows)
    {
        foreach (var r in rows) sink.Add(r);
    }

    private sealed class AthleteStats
    {
        public required string Slug { get; init; }
        public required string Name { get; init; }
        public DateTime? DobUtc { get; init; }
        public double? ChronoAge { get; init; }
        public double? LowestPhenoAge { get; init; }
        public double? AgeReduction { get; init; }
        public int SubmissionCount { get; init; }
        public string? Division { get; init; }
        public string? Generation { get; init; }
        public string? Exclusive { get; init; }
        public double[]? BestMarkerValues { get; init; }
        public double? PhenoAgeDiffFromBaseline { get; init; }
        public double? CrowdAge { get; init; }
        public int CrowdCount { get; init; }
    }

    private enum BadgeScope
    {
        Global,
        Division,
        Generation,
        Exclusive
    }

    private enum SortDir
    {
        Asc,
        Desc
    }

    private sealed record BadgeDefinition(
        string Label,
        BadgeScope Scope,
        int TopN,
        Func<AthleteStats, bool> Eligibility,
        Func<AthleteStats, double?> Metric,
        SortDir SortDirection,
        IComparer<AthleteStats>? TieBreaker,
        string MetricKey
    );

    private static string ComputeRuleHash(string signature)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(signature));
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    private static string BuildRankedRuleHash(
        string label,
        string leagueCategory,
        string metricKey,
        int topN,
        SortDir sort,
        string tieBreakKey,
        string? generationBandsVersion)
    {
        var sb = new StringBuilder();
        sb.Append("label=").Append(label)
            .Append("|category=").Append(leagueCategory)
            .Append("|metric=").Append(metricKey)
            .Append("|topN=").Append(topN)
            .Append("|sort=").Append(sort == SortDir.Asc ? "asc" : "desc")
            .Append("|tie=").Append(tieBreakKey);

        if (leagueCategory == "Generation")
        {
            sb.Append("|genBands=").Append(generationBandsVersion ?? "gen-v1");
        }

        return ComputeRuleHash(sb.ToString());
    }

    private static string BuildDomainRuleHash(string label, string domainKey)
    {
        var sig = $"label={label}|category=Global|metric=domain:{domainKey}|agg=min|ties=allowed|place=1";
        return ComputeRuleHash(sig);
    }

    private static string BuildSubmissionsRuleHash(string label, bool isThreshold, int threshold = 0)
    {
        string sig = isThreshold
            ? $"label={label}|category=Global|rule=threshold_submissions>={threshold}"
            : $"label={label}|category=Global|metric=submission_count|agg=max|ties=allowed|place=1";
        return ComputeRuleHash(sig);
    }

    private static string BuildPhenoDiffRuleHash(string label)
    {
        var sig = $"label={label}|category=Global|metric=pheno_last_minus_baseline|agg=min|requires_subs>=2|ties=allowed|place=1";
        return ComputeRuleHash(sig);
    }

    private static string BuildCrowdRuleHash(string label, string metricKey, string order, bool distinctValues, int decimals)
    {
        var sig = $"label={label}|category=Global|metric={metricKey}|order={order}|distinct_values={(distinctValues ? "yes" : "no")}|round={decimals}dp|tiers=top3|ties=allowed";
        return ComputeRuleHash(sig);
    }

    private static IReadOnlyList<BadgeDefinition> BuildBadgeDefinitions()
    {
        var compTie = Comparer<AthleteStats>.Create((a, b) =>
        {
            int dobCmp = CompareNullable(a.DobUtc, b.DobUtc, (x, y) => DateTime.Compare(x, y));
            if (dobCmp != 0) return dobCmp;
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        return new[]
        {
            new BadgeDefinition(
                Label: "Chronological Age – Oldest",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.ChronoAge.HasValue,
                Metric: a => a.ChronoAge,
                SortDirection: SortDir.Desc,
                TieBreaker: compTie,
                MetricKey: "ChronoAge"),

            new BadgeDefinition(
                Label: "Chronological Age – Youngest",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.ChronoAge.HasValue,
                Metric: a => a.ChronoAge,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie,
                MetricKey: "ChronoAge"),

            new BadgeDefinition(
                Label: "PhenoAge – Lowest",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.LowestPhenoAge.HasValue,
                Metric: a => a.LowestPhenoAge,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie,
                MetricKey: "PhenoAge"),

            new BadgeDefinition(
                Label: "Age Reduction",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.AgeReduction.HasValue,
                Metric: a => a.AgeReduction,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie,
                MetricKey: "AgeReduction"),

            new BadgeDefinition(
                Label: "Age Reduction",
                Scope: BadgeScope.Division,
                TopN: 3,
                Eligibility: a => a.AgeReduction.HasValue && !string.IsNullOrWhiteSpace(a.Division),
                Metric: a => a.AgeReduction,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie,
                MetricKey: "AgeReduction"),

            new BadgeDefinition(
                Label: "Age Reduction",
                Scope: BadgeScope.Generation,
                TopN: 3,
                Eligibility: a => a.AgeReduction.HasValue && !string.IsNullOrWhiteSpace(a.Generation),
                Metric: a => a.AgeReduction,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie,
                MetricKey: "AgeReduction"),

            new BadgeDefinition(
                Label: "Age Reduction",
                Scope: BadgeScope.Exclusive,
                TopN: 3,
                Eligibility: a => a.AgeReduction.HasValue && !string.IsNullOrWhiteSpace(a.Exclusive),
                Metric: a => a.AgeReduction,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie,
                MetricKey: "AgeReduction"),
        };
    }

    private Dictionary<string, AthleteStats> BuildAthleteStats(DateTime asOf)
    {
        var core = PhenoStatsCalculator.BuildAll(_athletes.Athletes, asOf);
        var result = new Dictionary<string, AthleteStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in core)
        {
            var r = kv.Value;
            result[kv.Key] = new AthleteStats
            {
                Slug = kv.Key,
                Name = r.Name,
                DobUtc = r.DobUtc,
                ChronoAge = r.ChronoAge,
                LowestPhenoAge = r.LowestPhenoAge,
                AgeReduction = r.AgeReduction,
                SubmissionCount = r.SubmissionCount,
                Division = r.Division,
                Generation = r.Generation,
                Exclusive = r.Exclusive,
                BestMarkerValues = r.BestMarkerValues,
                PhenoAgeDiffFromBaseline = r.PhenoAgeDiffFromBaseline,
                CrowdAge = r.CrowdAge,
                CrowdCount = r.CrowdCount
            };
        }

        return result;
    }

    private static IEnumerable<(string Category, string? Value, List<AthleteStats> Group)> GroupByLeague(
        IEnumerable<AthleteStats> items, BadgeScope scope)
    {
        var dict = new Dictionary<(string Cat, string? Val), List<AthleteStats>>();

        foreach (var s in items)
        {
            (string Cat, string? Val)? key = scope switch
            {
                BadgeScope.Division => !string.IsNullOrWhiteSpace(s.Division) ? ("Division", s.Division) : null,
                BadgeScope.Generation => !string.IsNullOrWhiteSpace(s.Generation) ? ("Generation", s.Generation) : null,
                BadgeScope.Exclusive => !string.IsNullOrWhiteSpace(s.Exclusive) ? ("Exclusive", s.Exclusive) : null,
                _ => ("Global", null)
            };
            if (key is null) continue;

            if (!dict.TryGetValue(key.Value, out var list))
            {
                list = new List<AthleteStats>();
                dict[key.Value] = list;
            }

            list.Add(s);
        }

        foreach (var kv in dict)
            yield return (kv.Key.Cat, kv.Key.Val, kv.Value);
    }

    private static List<AthleteStats> SelectTop(IEnumerable<AthleteStats> pool, BadgeDefinition def)
    {
        var filtered = pool.Where(def.Eligibility).ToList();

        filtered.Sort((a, b) =>
        {
            var ma = def.Metric(a);
            var mb = def.Metric(b);

            int cmp = CompareNullable(ma, mb, (x, y) => x.CompareTo(y));
            if (def.SortDirection == SortDir.Desc) cmp = -cmp;
            if (cmp != 0) return cmp;

            if (def.TieBreaker is not null)
                return def.TieBreaker.Compare(a, b);

            return string.Compare(a.Slug, b.Slug, StringComparison.Ordinal);
        });

        return filtered.Take(Math.Max(def.TopN, 0)).ToList();
    }

    private static int CompareNullable<T>(T? a, T? b, Func<T, T, int> cmp) where T : struct
    {
        bool ha = a.HasValue, hb = b.HasValue;
        if (ha && hb) return cmp(a.Value, b.Value);
        if (ha && !hb) return -1;
        if (!ha && hb) return 1;
        return 0;
    }

    private static void AddDomainAwards(
        Dictionary<string, AthleteStats> stats,
        List<AwardRow> awards)
    {
        (string Label, string Key, Func<double[], double> Score)[] domains =
        {
            ("Best Domain – Liver", "liver", PhenoAgeHelper.CalculateLiverPhenoAgeContributor),
            ("Best Domain – Kidney", "kidney", PhenoAgeHelper.CalculateKidneyPhenoAgeContributor),
            ("Best Domain – Metabolic", "metabolic", PhenoAgeHelper.CalculateMetabolicPhenoAgeContributor),
            ("Best Domain – Inflammation", "inflammation", PhenoAgeHelper.CalculateInflammationPhenoAgeContributor),
            ("Best Domain – Immune", "immune", PhenoAgeHelper.CalculateImmunePhenoAgeContributor),
        };

        foreach (var (label, key, scorer) in domains)
        {
            var candidates = stats.Values
                .Where(s => s.BestMarkerValues is not null)
                .Select(s => new { s.Slug, Score = scorer(s.BestMarkerValues!) })
                .ToList();

            if (candidates.Count == 0) continue;

            var best = candidates.Min(x => x.Score);
            var holders = candidates.Where(x => x.Score == best)
                .Select(x => x.Slug);

            var ruleHash = BuildDomainRuleHash(label, key);

            foreach (var slug in holders)
            {
                awards.Add(new AwardRow
                {
                    BadgeLabel = label,
                    LeagueCategory = "Global",
                    LeagueValue = null,
                    Place = 1,
                    AthleteSlug = slug,
                    DefinitionHash = ruleHash
                });
            }
        }
    }

    private static void AddSubmissionAwards(
        Dictionary<string, AthleteStats> stats,
        List<AwardRow> awards)
    {
        var threshold = 2;
        var thresholdHash = BuildSubmissionsRuleHash("≥2 Submissions", isThreshold: true, threshold: threshold);

        foreach (var s in stats.Values.Where(s => s.SubmissionCount >= threshold))
        {
            awards.Add(new AwardRow
            {
                BadgeLabel = "≥2 Submissions",
                LeagueCategory = "Global",
                LeagueValue = null,
                Place = null,
                AthleteSlug = s.Slug,
                DefinitionHash = thresholdHash
            });
        }

        var maxCount = stats.Values.Max(s => s.SubmissionCount);
        if (maxCount > 0)
        {
            var mostHash = BuildSubmissionsRuleHash("Most Submissions", isThreshold: false);
            foreach (var s in stats.Values.Where(s => s.SubmissionCount == maxCount))
            {
                awards.Add(new AwardRow
                {
                    BadgeLabel = "Most Submissions",
                    LeagueCategory = "Global",
                    LeagueValue = null,
                    Place = 1,
                    AthleteSlug = s.Slug,
                    DefinitionHash = mostHash
                });
            }
        }
    }

    private static void AddPhenoDiffAwards(
        Dictionary<string, AthleteStats> stats,
        List<AwardRow> awards)
    {
        var candidates = stats.Values
            .Where(s => s.PhenoAgeDiffFromBaseline.HasValue && s.SubmissionCount >= 2)
            .Select(s => new { s.Slug, Diff = s.PhenoAgeDiffFromBaseline!.Value })
            .ToList();

        if (candidates.Count == 0) return;

        var best = candidates.Min(x => x.Diff);
        var ruleHash = BuildPhenoDiffRuleHash("PhenoAge Best Improvement");

        foreach (var x in candidates.Where(x => x.Diff == best))
        {
            awards.Add(new AwardRow
            {
                BadgeLabel = "PhenoAge Best Improvement",
                LeagueCategory = "Global",
                LeagueValue = null,
                Place = 1,
                AthleteSlug = x.Slug,
                DefinitionHash = ruleHash
            });
        }
    }

    private static void AddCrowdAwards(
        Dictionary<string, AthleteStats> stats,
        List<AwardRow> awards)
    {
        var withGuesses = stats.Values
            .Where(s => s.CrowdCount > 0 && s.CrowdAge.HasValue)
            .ToList();

        if (withGuesses.Count == 0) return;

        var topCounts = withGuesses.Select(s => s.CrowdCount)
            .Where(c => c > 0)
            .Distinct()
            .OrderByDescending(c => c)
            .Take(3)
            .ToList();
        var mostGuessedHash = BuildCrowdRuleHash("Crowd – Most Guessed", "CrowdCount", "desc", true, 0);
        AddTier(withGuesses, topCounts, s => s.CrowdCount, "Crowd – Most Guessed", mostGuessedHash, awards);

        var positiveGaps = withGuesses
            .Where(s => s.ChronoAge.HasValue && s.CrowdAge.HasValue)
            .Select(s => Math.Round(s.ChronoAge!.Value - s.CrowdAge!.Value, 2, MidpointRounding.AwayFromZero))
            .Where(g => g > 0)
            .Distinct()
            .OrderByDescending(g => g)
            .Take(3)
            .ToList();
        var ageGapHash = BuildCrowdRuleHash("Crowd – Age Gap (Chrono−Crowd)", "ChronoMinusCrowdAge", "desc", true, 2);
        AddTier(withGuesses, positiveGaps, s => Math.Round(s.ChronoAge!.Value - s.CrowdAge!.Value, 2, MidpointRounding.AwayFromZero), "Crowd – Age Gap (Chrono−Crowd)", ageGapHash, awards);

        var lowestAges = withGuesses
            .Select(s => Math.Round(s.CrowdAge!.Value, 2, MidpointRounding.AwayFromZero))
            .Distinct()
            .OrderBy(a => a)
            .Take(3)
            .ToList();
        var lowestAgeHash = BuildCrowdRuleHash("Crowd – Lowest Crowd Age", "CrowdAge", "asc", true, 2);
        AddTier(withGuesses, lowestAges, s => Math.Round(s.CrowdAge!.Value, 2, MidpointRounding.AwayFromZero), "Crowd – Lowest Crowd Age", lowestAgeHash, awards);

        static void AddTier<T>(
            List<AthleteStats> pool,
            List<T> top3Values,
            Func<AthleteStats, T> selector,
            string label,
            string ruleHash,
            List<AwardRow> sink)
            where T : notnull, IEquatable<T>
        {
            for (int i = 0; i < top3Values.Count; i++)
            {
                int place = i + 1;
                var v = top3Values[i];
                foreach (var s in pool.Where(s => selector(s).Equals(v)))
                {
                    sink.Add(new AwardRow
                    {
                        BadgeLabel = label,
                        LeagueCategory = "Global",
                        LeagueValue = null,
                        Place = place,
                        AthleteSlug = s.Slug,
                        DefinitionHash = ruleHash
                    });
                }
            }
        }
    }

    public void Dispose()
    {
        _athletes.AthletesChanged -= OnAthletesChanged;
        GC.SuppressFinalize(this);
    }
}
