using System.Text;
using System.Text.Json;
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
    private readonly SqliteConnection _sqlite;
    private readonly DateTime _asOfUtc;

    private const string AwardsTable = "BadgeAwards";

    public BadgeDataService(AthleteDataService athletes)
    {
        _athletes = athletes ?? throw new ArgumentNullException(nameof(athletes));
        _asOfUtc = DateTime.UtcNow.Date;

        var dbPath = Path.Combine(EnvironmentHelpers.GetDataDir(), "LongevityWorldCup.db");
        _sqlite = new SqliteConnection($"Data Source={dbPath}");
        _sqlite.Open();

        EnsureTables();
        ComputeAndPersistAwards();
        _athletes.RefreshBadgesFromDatabase();

        // NEW: when athlete files change at runtime, recompute and refresh badges
        _athletes.AthletesChanged += OnAthletesChanged;
    }

    private void OnAthletesChanged()
    {
        // Recompute awards based on the latest athlete data,
        // then hydrate the latest awards back into athlete JSONs.
        ComputeAndPersistAwards();
        _athletes.RefreshBadgesFromDatabase();
    }

    /// <summary>
    /// Ensure human-friendly awards table exists.
    /// </summary>
    private void EnsureTables()
    {
        using var cmd = _sqlite.CreateCommand();
        cmd.CommandText = $@"
CREATE TABLE IF NOT EXISTS {AwardsTable} (
    BadgeLabel     TEXT NOT NULL,   -- human readable label (e.g., 'Age Reduction', 'Chronological Age – Oldest')
    LeagueCategory TEXT NOT NULL,   -- 'Global' | 'Division' | 'Generation' | 'Exclusive'
    LeagueValue    TEXT NULL,       -- value for the category (e.g., 'Men''s', 'Gen X', 'Prosperan'), NULL for Global
    Place          INTEGER NULL,    -- 1/2/3 for ranked badges; NULL for flag-style badges
    AthleteSlug    TEXT NOT NULL,
    DefinitionHash TEXT NULL,       -- per-rule hash (varies only when the underlying rule changes)
    UpdatedAt      TEXT NOT NULL,
    PRIMARY KEY (BadgeLabel, LeagueCategory, LeagueValue, Place, AthleteSlug)
);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Compute the snapshot and replace BadgeAwards in a single transaction.
    /// </summary>
    public void ComputeAndPersistAwards()
    {
        var stats = BuildAthleteStats(_asOfUtc);
        var defs = BuildBadgeDefinitions();

        // Collect human-friendly rows here
        var awards = new List<AwardRow>(capacity: 4096);

        // 1) Ranked Top-N badges (global/division/generation/exclusive)
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

                    var genVersion = cat == "Generation" ? "gen-v1" : null; // bump if generation bands change in future
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

        // 2) Domain winners (ties allowed) => Place = 1, LeagueCategory = Global
        AddDomainAwards(stats, awards);

        // 3) Submission badges
        AddSubmissionAwards(stats, awards);

        // 4) PhenoAge best improvement => Place = 1 (ties allowed), Global
        AddPhenoDiffAwards(stats, awards);

        // 5) Crowd badges (tiers) => Place = 1/2/3, Global
        AddCrowdAwards(stats, awards);

        // Persist (replace all rows)
        var now = DateTime.UtcNow.ToString("o");

        using var tx = _sqlite.BeginTransaction();

        using (var del = _sqlite.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = $"DELETE FROM {AwardsTable};";
            del.ExecuteNonQuery();
        }

        using (var ins = _sqlite.CreateCommand())
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
    }

    private sealed class AwardRow
    {
        public required string BadgeLabel { get; init; } // human label
        public required string LeagueCategory { get; init; } // Global | Division | Generation | Exclusive
        public string? LeagueValue { get; init; } // null for Global
        public int? Place { get; init; } // 1/2/3 or null
        public required string AthleteSlug { get; init; }
        public string? DefinitionHash { get; init; } // per-rule hash
    }

    private static void AddRange(List<AwardRow> sink, IEnumerable<AwardRow> rows)
    {
        foreach (var r in rows) sink.Add(r);
    }

    // ===== Models & Definitions ============================================

    private sealed class AthleteStats
    {
        public required string Slug { get; init; }
        public required string Name { get; init; }
        public DateTime? DobUtc { get; init; }
        public double? ChronoAge { get; init; } // years
        public double? LowestPhenoAge { get; init; } // years
        public double? AgeReduction { get; init; } // pheno - chrono (lower is better)
        public int SubmissionCount { get; init; }

        // Grouping
        public string? Division { get; init; }
        public string? Generation { get; init; }
        public string? Exclusive { get; init; }

        // For domain badges and redemption arc
        public double[]? BestMarkerValues { get; init; } // [ageYears, Alb, Creat, Glu, ln(CRP/10), WBC, Lymph, MCV, RDW, ALP]
        public double? PhenoAgeDiffFromBaseline { get; init; } // bestPheno - firstSubmissionPheno

        // Crowd stats (for crowd badges)
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
        string Label, // human-friendly label used as identifier (you stated: label will not change)
        BadgeScope Scope,
        int TopN,
        Func<AthleteStats, bool> Eligibility,
        Func<AthleteStats, double?> Metric,
        SortDir SortDirection,
        IComparer<AthleteStats>? TieBreaker,
        string MetricKey // stable metric identifier (e.g., 'ChronoAge', 'PhenoAge', 'AgeReduction')
    );

    // ===== Rule-hash helpers ===============================================

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
        // Compose a stable textual signature for ranked (TopN) badges
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
        // Domain contributors: min contributor, ties allowed, global, place=1
        var sig = $"label={label}|category=Global|metric=domain:{domainKey}|agg=min|ties=allowed|place=1";
        return ComputeRuleHash(sig);
    }

    private static string BuildSubmissionsRuleHash(string label, bool isThreshold, int threshold = 0)
    {
        // ≥2 submissions (flag): threshold; Most Submissions: max submissions, ties allowed
        string sig = isThreshold
            ? $"label={label}|category=Global|rule=threshold_submissions>={threshold}"
            : $"label={label}|category=Global|metric=submission_count|agg=max|ties=allowed|place=1";
        return ComputeRuleHash(sig);
    }

    private static string BuildPhenoDiffRuleHash(string label)
    {
        // Best improvement: min (bestPheno - baselinePheno), requires >=2 subs, ties allowed, global
        var sig = $"label={label}|category=Global|metric=pheno_best_minus_baseline|agg=min|requires_subs>=2|ties=allowed|place=1";
        return ComputeRuleHash(sig);
    }

    private static string BuildCrowdRuleHash(string label, string metricKey, string order, bool distinctValues, int decimals)
    {
        // Crowd badges: distinct value tiers, rounding rules included for stability
        var sig = $"label={label}|category=Global|metric={metricKey}|order={order}|distinct_values={(distinctValues ? "yes" : "no")}|round={decimals}dp|tiers=top3|ties=allowed";
        return ComputeRuleHash(sig);
    }

    // ===== Definitions ======================================================

    private static IReadOnlyList<BadgeDefinition> BuildBadgeDefinitions()
    {
        // Tie-breaker: competition rules (older DOB wins, then name)
        var compTie = Comparer<AthleteStats>.Create((a, b) =>
        {
            int dobCmp = CompareNullable(a.DobUtc, b.DobUtc, (x, y) => DateTime.Compare(x, y));
            if (dobCmp != 0) return dobCmp;
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        return new[]
        {
            // Chronological Age – Oldest (global)
            new BadgeDefinition(
                Label: "Chronological Age – Oldest",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.ChronoAge.HasValue,
                Metric: a => a.ChronoAge,
                SortDirection: SortDir.Desc,
                TieBreaker: compTie,
                MetricKey: "ChronoAge"),

            // Chronological Age – Youngest (global)
            new BadgeDefinition(
                Label: "Chronological Age – Youngest",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.ChronoAge.HasValue,
                Metric: a => a.ChronoAge,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie,
                MetricKey: "ChronoAge"),

            // PhenoAge – Lowest (global)
            new BadgeDefinition(
                Label: "PhenoAge – Lowest",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.LowestPhenoAge.HasValue,
                Metric: a => a.LowestPhenoAge,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie,
                MetricKey: "PhenoAge"),

            // Age Reduction (global)
            new BadgeDefinition(
                Label: "Age Reduction",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.AgeReduction.HasValue,
                Metric: a => a.AgeReduction,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie,
                MetricKey: "AgeReduction"),

            // Age Reduction by Division
            new BadgeDefinition(
                Label: "Age Reduction",
                Scope: BadgeScope.Division,
                TopN: 3,
                Eligibility: a => a.AgeReduction.HasValue && !string.IsNullOrWhiteSpace(a.Division),
                Metric: a => a.AgeReduction,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie,
                MetricKey: "AgeReduction"),

            // Age Reduction by Generation
            new BadgeDefinition(
                Label: "Age Reduction",
                Scope: BadgeScope.Generation,
                TopN: 3,
                Eligibility: a => a.AgeReduction.HasValue && !string.IsNullOrWhiteSpace(a.Generation),
                Metric: a => a.AgeReduction,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie,
                MetricKey: "AgeReduction"),

            // Age Reduction by Exclusive league
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

    // ===== Stat projection ==================================================

    private Dictionary<string, AthleteStats> BuildAthleteStats(DateTime asOf)
    {
        var result = new Dictionary<string, AthleteStats>(StringComparer.OrdinalIgnoreCase);

        foreach (var o in _athletes.Athletes.OfType<JsonObject>())
        {
            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;

            var name = o["Name"]?.GetValue<string>() ?? slug;

            // DOB
            DateTime? dob = null;
            if (o["DateOfBirth"] is JsonObject dobNode)
            {
                try
                {
                    int y = dobNode["Year"]!.GetValue<int>();
                    int m = dobNode["Month"]!.GetValue<int>();
                    int d = dobNode["Day"]!.GetValue<int>();
                    dob = new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc);
                }
                catch
                {
                    /* ignore invalid */
                }
            }

            double? chrono = null;
            if (dob.HasValue)
                chrono = Math.Round((asOf - dob.Value.Date).TotalDays / 365.2425, 2);

            // Build best marker-values and baseline/best PhenoAge from Biomarkers
            int submissionCount = 0;
            double lowestPheno = double.PositiveInfinity;
            double earliestPheno = double.PositiveInfinity;
            DateTime? earliestDate = null;
            double[]? bestMarkerValues = null;

// NEW: track per-marker bests (mix-and-match across complete sets)
            double? bestAlb = null,
                bestCreat = null,
                bestGlu = null,
                bestCrp = null,
                bestWbc = null,
                bestLym = null,
                bestMcv = null,
                bestRdw = null,
                bestAlp = null;

            if (o["Biomarkers"] is JsonArray biomArr)
            {
                foreach (var entry in biomArr.OfType<JsonObject>())
                {
                    // Parse entry date (roundtrip if present)
                    var entryDate = asOf;
                    var ds = entry["Date"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(ds) &&
                        DateTime.TryParse(ds, null, DateTimeStyles.RoundtripKind, out var parsed))
                    {
                        entryDate = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
                    }

                    // Age at the date of the test
                    double AgeAt(DateTime d) => dob.HasValue ? (d.Date - dob.Value.Date).TotalDays / 365.2425 : double.NaN;

                    // Read raw biomarkers; require positive CRP to compute (== "complete set")
                    if (TryGet(entry, "AlbGL", out var alb) &&
                        TryGet(entry, "CreatUmolL", out var creat) &&
                        TryGet(entry, "GluMmolL", out var glu) &&
                        TryGet(entry, "CrpMgL", out var crpMgL) &&
                        TryGet(entry, "Wbc1000cellsuL", out var wbc) &&
                        TryGet(entry, "LymPc", out var lym) &&
                        TryGet(entry, "McvFL", out var mcv) &&
                        TryGet(entry, "RdwPc", out var rdw) &&
                        TryGet(entry, "AlpUL", out var alp) &&
                        crpMgL > 0 && !double.IsNaN(AgeAt(entryDate)))
                    {
                        // Count only complete biomarker sets (matches FE)
                        submissionCount++;

                        // PhenoAge for baseline/lowest calculations
                        var ph = PhenoAgeHelper.CalculatePhenoAgeFromRaw(
                            AgeAt(entryDate), alb, creat, glu, crpMgL, wbc, lym, mcv, rdw, alp);

                        if (!double.IsNaN(ph) && !double.IsInfinity(ph))
                        {
                            // track earliest valid pheno as "baseline"
                            if (!earliestDate.HasValue || entryDate < earliestDate.Value)
                            {
                                earliestDate = entryDate;
                                earliestPheno = ph;
                            }

                            // best (minimum) PhenoAge across real submissions
                            if (ph < lowestPheno)
                            {
                                lowestPheno = ph;
                            }
                        }

                        // --- NEW: update per-marker bests (mirrors old FE rules) ---
                        // Albumin & Lymphocytes: higher is better
                        if (!bestAlb.HasValue || alb > bestAlb.Value) bestAlb = alb;
                        if (!bestLym.HasValue || lym > bestLym.Value) bestLym = lym;

                        // Everything else: lower is better
                        if (!bestCreat.HasValue || creat < bestCreat.Value) bestCreat = creat;
                        if (!bestGlu.HasValue || glu < bestGlu.Value) bestGlu = glu;
                        if (!bestCrp.HasValue || crpMgL < bestCrp.Value) bestCrp = crpMgL;
                        if (!bestWbc.HasValue || wbc < bestWbc.Value) bestWbc = wbc;
                        if (!bestMcv.HasValue || mcv < bestMcv.Value) bestMcv = mcv;
                        if (!bestRdw.HasValue || rdw < bestRdw.Value) bestRdw = rdw;
                        if (!bestAlp.HasValue || alp < bestAlp.Value) bestAlp = alp;
                    }
                }

                // Build BestMarkerValues exactly like the legacy FE did:
                // use "today's" chronological age + per-marker bests (mix-and-match), and ln(CRP/10).
                if (bestAlb.HasValue && bestCreat.HasValue && bestGlu.HasValue && bestCrp.HasValue &&
                    bestWbc.HasValue && bestLym.HasValue && bestMcv.HasValue && bestRdw.HasValue && bestAlp.HasValue &&
                    bestCrp.Value > 0 && chrono.HasValue)
                {
                    var lnCrpOver10 = Math.Log(bestCrp.Value / 10.0);
                    bestMarkerValues = new[]
                    {
                        chrono.Value, // <-- FE used current chronological age here
                        bestAlb.Value,
                        bestCreat.Value,
                        bestGlu.Value,
                        lnCrpOver10,
                        bestWbc.Value,
                        bestLym.Value,
                        bestMcv.Value,
                        bestRdw.Value,
                        bestAlp.Value
                    };
                }
            }

            if (double.IsNaN(lowestPheno) || double.IsInfinity(lowestPheno))
                lowestPheno = chrono ?? double.PositiveInfinity;

            double? ageReduction = (chrono.HasValue && !double.IsInfinity(lowestPheno))
                ? Math.Round(lowestPheno - chrono.Value, 2)
                : (double?)null;

            double? lowestPhenoRounded = double.IsInfinity(lowestPheno) ? null : Math.Round(lowestPheno, 2);

            double? phenoDiffFromBaseline = null;
            if (!double.IsInfinity(earliestPheno) && !double.IsInfinity(lowestPheno))
            {
                phenoDiffFromBaseline = Math.Round(lowestPheno - earliestPheno, 2);
            }

            // Crowd stats hydrated by AthleteDataService (CrowdAge, CrowdCount)
            double? crowdAge = null;
            int crowdCount = 0;
            try
            {
                if (o["CrowdAge"] is JsonValue jvAge && jvAge.TryGetValue<double>(out var ca))
                    crowdAge = ca;
                if (o["CrowdCount"] is JsonValue jvCnt && jvCnt.TryGetValue<int>(out var cc))
                    crowdCount = cc;
            }
            catch
            {
                /* ignore */
            }

            // Generation fallback from DOB (mirrors FE)
            var generation = TryGetString(o, "Generation");
            if (string.IsNullOrWhiteSpace(generation) && dob.HasValue)
            {
                generation = GetGenerationFromBirthYear(dob.Value.Year);
            }

            result[slug] = new AthleteStats
            {
                Slug = slug,
                Name = name,
                DobUtc = dob,
                ChronoAge = chrono,
                LowestPhenoAge = lowestPhenoRounded,
                AgeReduction = ageReduction,
                SubmissionCount = submissionCount,
                Division = TryGetString(o, "Division"),
                Generation = generation,
                Exclusive = TryGetString(o, "ExclusiveLeague"),
                BestMarkerValues = bestMarkerValues,
                PhenoAgeDiffFromBaseline = phenoDiffFromBaseline,
                CrowdAge = crowdAge,
                CrowdCount = crowdCount
            };
        }

        return result;

        static bool TryGet(JsonObject o, string key, out double v)
        {
            v = 0;
            try
            {
                var n = o[key];
                if (n is null) return false;
                v = n.GetValue<double>();
                return !double.IsNaN(v) && !double.IsInfinity(v);
            }
            catch
            {
                return false;
            }
        }

        static string? TryGetString(JsonObject o, string key)
        {
            try
            {
                var n = o[key];
                if (n is null) return null;
                var s = n.GetValue<string?>();
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Groups athletes by human-friendly league (Category + Value).
    /// </summary>
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
        if (ha && !hb) return -1; // value < null
        if (!ha && hb) return 1; // null > value
        return 0; // both null
    }

    // ===== Additional badge calculators ====================================

    private static void AddDomainAwards(
        Dictionary<string, AthleteStats> stats,
        List<AwardRow> awards)
    {
        // Domain contributors same as FE pheno-age.js (use helpers)
        const double EPS = 1e-9;

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
            var holders = candidates.Where(x => Math.Abs(x.Score - best) <= EPS)
                .Select(x => x.Slug);

            var ruleHash = BuildDomainRuleHash(label, key);

            foreach (var slug in holders)
            {
                awards.Add(new AwardRow
                {
                    BadgeLabel = label,
                    LeagueCategory = "Global",
                    LeagueValue = null,
                    Place = 1, // winners share place #1
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
        // ≥2 Submissions: everyone with >=2 submissions (flag, Place=null)
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

        // Most Submissions: max submissions (ties allowed), Place=1
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
        // PhenoAge Best Improvement: lowest (most negative) Δ from baseline (ties allowed), Place=1
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
        // Distinct values; top-3; ties allowed. Place=1/2/3, Global.
        var withGuesses = stats.Values
            .Where(s => s.CrowdCount > 0 && s.CrowdAge.HasValue)
            .ToList();

        if (withGuesses.Count == 0) return;

        // Crowd – Most Guessed (by CrowdCount) - top-3 distinct desc
        var topCounts = withGuesses.Select(s => s.CrowdCount)
            .Where(c => c > 0)
            .Distinct()
            .OrderByDescending(c => c)
            .Take(3)
            .ToList();
        var mostGuessedHash = BuildCrowdRuleHash("Crowd – Most Guessed", metricKey: "CrowdCount", order: "desc", distinctValues: true, decimals: 0);
        AddTier(withGuesses, topCounts, s => s.CrowdCount, "Crowd – Most Guessed", mostGuessedHash, awards);

        // Crowd – Age Gap (Chrono−Crowd) positive gaps - top-3 distinct desc
        var positiveGaps = withGuesses
            .Where(s => s.ChronoAge.HasValue && s.CrowdAge.HasValue)
            .Select(s => Math.Round(s.ChronoAge!.Value - s.CrowdAge!.Value, 2))
            .Where(g => g > 0)
            .Distinct()
            .OrderByDescending(g => g)
            .Take(3)
            .ToList();
        var ageGapHash = BuildCrowdRuleHash("Crowd – Age Gap (Chrono−Crowd)", metricKey: "ChronoMinusCrowdAge", order: "desc", distinctValues: true, decimals: 2);
        AddTier(withGuesses, positiveGaps, s => Math.Round(s.ChronoAge!.Value - s.CrowdAge!.Value, 2), "Crowd – Age Gap (Chrono−Crowd)", ageGapHash, awards);

        // Crowd – Lowest Crowd Age - top-3 distinct asc
        var lowestAges = withGuesses
            .Select(s => Math.Round(s.CrowdAge!.Value, 2))
            .Distinct()
            .OrderBy(a => a)
            .Take(3)
            .ToList();
        var lowestAgeHash = BuildCrowdRuleHash("Crowd – Lowest Crowd Age", metricKey: "CrowdAge", order: "asc", distinctValues: true, decimals: 2);
        AddTier(withGuesses, lowestAges, s => Math.Round(s.CrowdAge!.Value, 2), "Crowd – Lowest Crowd Age", lowestAgeHash, awards);

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
                int place = i + 1; // 1=gold, 2=silver, 3=bronze
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

    // ===== Utilities ========================================================

    // Mirrors frontend getGeneration(birthYear)
    private static string? GetGenerationFromBirthYear(int birthYear)
    {
        if (birthYear >= 1928 && birthYear <= 1945) return "Silent Generation";
        if (birthYear >= 1946 && birthYear <= 1964) return "Baby Boomers";
        if (birthYear >= 1965 && birthYear <= 1980) return "Gen X";
        if (birthYear >= 1981 && birthYear <= 1996) return "Millennials";
        if (birthYear >= 1997 && birthYear <= 2012) return "Gen Z";
        if (birthYear >= 2013) return "Gen Alpha";
        return null;
    }

    public void Dispose()
    {
        _athletes.AthletesChanged -= OnAthletesChanged; // NEW: unsubscribe
        _sqlite.Close();
        _sqlite.Dispose();
        GC.SuppressFinalize(this);
    }
}