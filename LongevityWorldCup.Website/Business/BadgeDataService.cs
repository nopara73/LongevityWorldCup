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
/// No event emission, no frontend changes. Run at service startup.
/// </summary>
public sealed class BadgeDataService : IDisposable
{
    private readonly AthleteDataService _athletes;
    private readonly SqliteConnection _sqlite;
    private readonly DateTime _asOfUtc;

    private const string TableName = "BadgeStatus";

    public BadgeDataService(AthleteDataService athletes)
    {
        _athletes = athletes ?? throw new ArgumentNullException(nameof(athletes));
        _asOfUtc = DateTime.UtcNow.Date;

        var dbPath = Path.Combine(EnvironmentHelpers.GetDataDir(), "LongevityWorldCup.db");
        _sqlite = new SqliteConnection($"Data Source={dbPath}");
        _sqlite.Open();

        EnsureTable();
        ComputeAndPersistSnapshot();
    }

    /// <summary>
    /// Creates the BadgeStatus table if it does not exist yet.
    /// </summary>
    private void EnsureTable()
    {
        using var cmd = _sqlite.CreateCommand();
        cmd.CommandText = $@"
CREATE TABLE IF NOT EXISTS {TableName} (
    BadgeCode      TEXT NOT NULL,
    ScopeKey       TEXT NOT NULL,
    HolderSlugs    TEXT NOT NULL,
    DefinitionHash TEXT NULL,
    UpdatedAt      TEXT NOT NULL,
    PRIMARY KEY (BadgeCode, ScopeKey)
);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Computes the snapshot and replaces the table contents in a single transaction.
    /// </summary>
    public void ComputeAndPersistSnapshot()
    {
        var stats = BuildAthleteStats(_asOfUtc);
        var defs  = BuildBadgeDefinitions();

        // Compute: BadgeCode -> ScopeKey -> List<slug>
        var snapshot = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);

        // 1) Top-N style badges (global/division/generation/exclusive)
        foreach (var def in defs)
        {
            if (def.Scope == BadgeScope.Global)
            {
                var selected = SelectTop(stats.Values, def);
                Add(snapshot, def.Id, "global", selected.Select(s => s.Slug));
            }
            else
            {
                foreach (var (scopeKey, group) in GroupByScope(stats.Values, def.Scope))
                {
                    var selected = SelectTop(group, def);
                    if (selected.Count > 0)
                        Add(snapshot, def.Id, scopeKey, selected.Select(s => s.Slug));
                }
            }
        }

        // 2) Additional badges (ties allowed, tiers, multi-holder etc.)
        AddDomainBadges(stats, snapshot);         // Liver/Kidney/Metabolic/Inflammation/Immune winners
        AddSubmissionBadges(stats, snapshot);     // The Regular (>=2), The Submittinator (max)
        AddPhenoDiffBadge(stats, snapshot);       // Redemption Arc (best improvement)
        AddCrowdBadges(stats, snapshot);          // Most Guessed / Age-Gap / Lowest Crowd Age (gold/silver/bronze)

        // Persist (replace all rows)
        var defHash = ComputeDefinitionHash(defs); // definition set for Top-N badges
        var now = DateTime.UtcNow.ToString("o");

        using var tx = _sqlite.BeginTransaction();

        using (var del = _sqlite.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = $"DELETE FROM {TableName};";
            del.ExecuteNonQuery();
        }

        using (var ins = _sqlite.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = $@"
INSERT INTO {TableName} (BadgeCode, ScopeKey, HolderSlugs, DefinitionHash, UpdatedAt)
VALUES (@b, @s, @h, @dh, @u);";
            var pB = ins.Parameters.Add("@b", SqliteType.Text);
            var pS = ins.Parameters.Add("@s", SqliteType.Text);
            var pH = ins.Parameters.Add("@h", SqliteType.Text);
            var pD = ins.Parameters.Add("@dh", SqliteType.Text);
            var pU = ins.Parameters.Add("@u", SqliteType.Text);

            foreach (var (badgeCode, scopes) in snapshot)
            {
                foreach (var (scopeKey, holders) in scopes)
                {
                    pB.Value = badgeCode;
                    pS.Value = scopeKey;
                    pH.Value = JsonSerializer.Serialize(holders); // JSON array of slugs, order = rank order
                    pD.Value = defHash;
                    pU.Value = now;
                    ins.ExecuteNonQuery();
                }
            }
        }

        tx.Commit();
    }

    private static void Add(
        Dictionary<string, Dictionary<string, List<string>>> map,
        string badgeCode, string scopeKey, IEnumerable<string> slugs)
    {
        if (!map.TryGetValue(badgeCode, out var scopes))
        {
            scopes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            map[badgeCode] = scopes;
        }
        if (!scopes.TryGetValue(scopeKey, out var list))
        {
            list = new List<string>();
            scopes[scopeKey] = list;
        }

        foreach (var s in slugs)
            if (!string.IsNullOrWhiteSpace(s))
                list.Add(s);
    }

    // ===== Models & Definitions ============================================

    private sealed class AthleteStats
    {
        public required string Slug { get; init; }
        public required string Name { get; init; }
        public DateTime? DobUtc { get; init; }
        public double? ChronoAge { get; init; }               // years
        public double? LowestPhenoAge { get; init; }          // years
        public double? AgeReduction { get; init; }            // pheno - chrono (lower is better)
        public int SubmissionCount { get; init; }

        // Grouping
        public string? Division { get; init; }
        public string? Generation { get; init; }
        public string? Exclusive { get; init; }

        // For domain badges and redemption arc
        public double[]? BestMarkerValues { get; init; }      // [ageYears, Alb, Creat, Glu, ln(CRP/10), WBC, Lymph, MCV, RDW, ALP]
        public double? PhenoAgeDiffFromBaseline { get; init; } // bestPheno - firstSubmissionPheno

        // Crowd stats (for crowd badges)
        public double? CrowdAge { get; init; }
        public int CrowdCount { get; init; }
    }

    private enum BadgeScope { Global, Division, Generation, Exclusive }
    private enum SortDir { Asc, Desc }

    private sealed record BadgeDefinition(
        string Id,                      // BadgeCode
        BadgeScope Scope,
        int TopN,
        Func<AthleteStats, bool> Eligibility,
        Func<AthleteStats, double?> Metric,
        SortDir SortDirection,
        IComparer<AthleteStats>? TieBreaker = null
    );

    private static string ComputeDefinitionHash(IEnumerable<BadgeDefinition> defs)
    {
        // Minimal stable hash from key properties (good enough for v1)
        var sb = new StringBuilder();
        foreach (var d in defs.OrderBy(x => x.Id, StringComparer.Ordinal))
            sb.Append($"{d.Id}|{d.Scope}|{d.TopN}|{d.SortDirection}\n");
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

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
            // Global Top3 by chronological age (oldest)
            new BadgeDefinition(
                Id: "oldest",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.ChronoAge.HasValue,
                Metric: a => a.ChronoAge,
                SortDirection: SortDir.Desc,
                TieBreaker: compTie),

            // Global Top3 by chronological age (youngest)
            new BadgeDefinition(
                Id: "youngest",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.ChronoAge.HasValue,
                Metric: a => a.ChronoAge,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie),

            // Global Top3 by lowest PhenoAge (biologically youngest)
            new BadgeDefinition(
                Id: "bio_youngest",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.LowestPhenoAge.HasValue,
                Metric: a => a.LowestPhenoAge,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie),

            // Global Top3 by competition metric: AgeReduction (pheno - chrono), lower is better
            new BadgeDefinition(
                Id: "ultimate_league",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.AgeReduction.HasValue,
                Metric: a => a.AgeReduction,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie),

            // Per-division Top3 by AgeReduction
            new BadgeDefinition(
                Id: "top3_by_division",
                Scope: BadgeScope.Division,
                TopN: 3,
                Eligibility: a => a.AgeReduction.HasValue && !string.IsNullOrWhiteSpace(a.Division),
                Metric: a => a.AgeReduction,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie),

            // Per-generation Top3 by AgeReduction
            new BadgeDefinition(
                Id: "top3_by_generation",
                Scope: BadgeScope.Generation,
                TopN: 3,
                Eligibility: a => a.AgeReduction.HasValue && !string.IsNullOrWhiteSpace(a.Generation),
                Metric: a => a.AgeReduction,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie),

            // Per-exclusive league Top3 by AgeReduction
            new BadgeDefinition(
                Id: "top3_by_exclusive",
                Scope: BadgeScope.Exclusive,
                TopN: 3,
                Eligibility: a => a.AgeReduction.HasValue && !string.IsNullOrWhiteSpace(a.Exclusive),
                Metric: a => a.AgeReduction,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie),
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
                catch { /* ignore invalid */ }
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

            if (o["Biomarkers"] is JsonArray biomArr)
            {
                foreach (var entry in biomArr.OfType<JsonObject>())
                {
                    submissionCount++;

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

                    // Read raw biomarkers; require positive CRP to compute
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

                            // best = minimum pheno; also capture markerValues for domain contributors
                            if (ph < lowestPheno)
                            {
                                lowestPheno = ph;
                                var lnCrpOver10 = Math.Log(crpMgL / 10.0);
                                bestMarkerValues = new[]
                                {
                                    AgeAt(entryDate), alb, creat, glu, lnCrpOver10, wbc, lym, mcv, rdw, alp
                                };
                            }
                        }
                    }
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

            // Crowd stats come hydrated by AthleteDataService into the JSON (CrowdAge, CrowdCount).
            double? crowdAge = null;
            int crowdCount = 0;
            try
            {
                if (o["CrowdAge"] is JsonValue jvAge && jvAge.TryGetValue<double>(out var ca))
                    crowdAge = ca;
                if (o["CrowdCount"] is JsonValue jvCnt && jvCnt.TryGetValue<int>(out var cc))
                    crowdCount = cc;
            }
            catch { /* ignore */ }

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
            catch { return false; }
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
            catch { return null; }
        }
    }

    private static IReadOnlyDictionary<string, List<AthleteStats>> GroupByScope(IEnumerable<AthleteStats> items, BadgeScope scope)
    {
        var dict = new Dictionary<string, List<AthleteStats>>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in items)
        {
            string? key = scope switch
            {
                BadgeScope.Division  => s.Division,
                BadgeScope.Generation=> s.Generation,
                BadgeScope.Exclusive => s.Exclusive,
                _ => "global"
            };
            if (string.IsNullOrWhiteSpace(key)) continue;

            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<AthleteStats>();
                dict[key] = list;
            }
            list.Add(s);
        }

        return dict;
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
        if (ha && !hb) return -1;      // value < null
        if (!ha && hb) return 1;       // null > value
        return 0;                       // both null
    }

    // ===== Additional badge calculators ====================================

    private static void AddDomainBadges(
        Dictionary<string, AthleteStats> stats,
        Dictionary<string, Dictionary<string, List<string>>> snapshot)
    {
        // We use PhenoAgeHelper domain contributor functions exactly like FE does in pheno-age.js.
        const double EPS = 1e-9;

        (string Id, Func<double[], double> Score)[] domains =
        {
            ("liver_best",         PhenoAgeHelper.CalculateLiverPhenoAgeContributor),
            ("kidney_best",        PhenoAgeHelper.CalculateKidneyPhenoAgeContributor),
            ("metabolic_best",     PhenoAgeHelper.CalculateMetabolicPhenoAgeContributor),
            ("inflammation_best",  PhenoAgeHelper.CalculateInflammationPhenoAgeContributor),
            ("immune_best",        PhenoAgeHelper.CalculateImmunePhenoAgeContributor),
        };

        foreach (var (id, scorer) in domains)
        {
            var candidates = stats.Values
                .Where(s => s.BestMarkerValues is not null)
                .Select(s => new { s.Slug, s.Name, Score = scorer(s.BestMarkerValues!) })
                .ToList();

            if (candidates.Count == 0) continue;

            var best = candidates.Min(x => x.Score);
            var holders = candidates
                .Where(x => Math.Abs(x.Score - best) <= EPS)
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => x.Slug)
                .ToList();

            if (holders.Count > 0)
                Add(snapshot, id, "global", holders);
        }
    }
    
    // Mirrors frontend window.getGeneration(birthYear) from misc.js.
    // Silent(1928–1945), Boomers(1946–1964), Gen X(1965–1980), Millennials(1981–1996), Gen Z(1997–2012), Gen Alpha(2013+).
    private static string? GetGenerationFromBirthYear(int birthYear)
    {
        if (birthYear >= 1928 && birthYear <= 1945) return "Silent Generation";
        if (birthYear >= 1946 && birthYear <= 1964) return "Baby Boomers";
        if (birthYear >= 1965 && birthYear <= 1980) return "Gen X";
        if (birthYear >= 1981 && birthYear <= 1996) return "Millennials";
        if (birthYear >= 1997 && birthYear <= 2012) return "Gen Z";
        if (birthYear >= 2013) return "Gen Alpha";
        return null; // unknown/edge years
    }

    private static void AddSubmissionBadges(
        Dictionary<string, AthleteStats> stats,
        Dictionary<string, Dictionary<string, List<string>>> snapshot)
    {
        // The Regular: everyone with >= 2 submissions (non-exclusive, many holders).
        var regulars = stats.Values
            .Where(s => s.SubmissionCount >= 2)
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => s.Slug)
            .ToList();

        if (regulars.Count > 0)
            Add(snapshot, "submission_over_two", "global", regulars);

        // The Submittinator: max submission count (ties allowed).
        var maxCount = stats.Values.Max(s => s.SubmissionCount);
        if (maxCount > 0)
        {
            var most = stats.Values
                .Where(s => s.SubmissionCount == maxCount)
                .OrderBy(s => s.Name, StringComparer.Ordinal)
                .Select(s => s.Slug)
                .ToList();

            if (most.Count > 0)
                Add(snapshot, "most_submissions", "global", most);
        }
    }

    private static void AddPhenoDiffBadge(
        Dictionary<string, AthleteStats> stats,
        Dictionary<string, Dictionary<string, List<string>>> snapshot)
    {
        // Redemption Arc: lowest (most negative) PhenoAge difference from the first submission (baseline). Ties allowed.
        var candidates = stats.Values
            .Where(s => s.PhenoAgeDiffFromBaseline.HasValue && s.SubmissionCount >= 2)
            .Select(s => new { s.Slug, s.Name, Diff = s.PhenoAgeDiffFromBaseline!.Value })
            .ToList();

        if (candidates.Count == 0) return;

        var best = candidates.Min(x => x.Diff);
        var holders = candidates
            .Where(x => x.Diff == best) // numbers are rounded to 2 decimals at build time
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => x.Slug)
            .ToList();

        if (holders.Count > 0)
            Add(snapshot, "pheno_age_diff_best", "global", holders);
    }

    private static void AddCrowdBadges(
        Dictionary<string, AthleteStats> stats,
        Dictionary<string, Dictionary<string, List<string>>> snapshot)
    {
        // We mirror FE logic in badges.js for crowd tiers (distinct values, ties allowed).
        var withGuesses = stats.Values
            .Where(s => s.CrowdCount > 0 && s.CrowdAge.HasValue)
            .ToList();

        if (withGuesses.Count == 0) return;

        // --- Most Guessed: top-3 distinct counts (desc) ---
        var topCounts = withGuesses
            .Select(s => s.CrowdCount)
            .Where(c => c > 0)
            .Distinct()
            .OrderByDescending(c => c)
            .Take(3)
            .ToList();

        AddTier(snapshot, "crowd_most_guessed", withGuesses, topCounts, s => s.CrowdCount);

        // --- Age-Gap: top-3 distinct positive (ChronoAge - CrowdAge), desc ---
        var positiveGaps = withGuesses
            .Where(s => s.ChronoAge.HasValue && s.CrowdAge.HasValue)
            .Select(s => Math.Round(s.ChronoAge!.Value - s.CrowdAge!.Value, 2))
            .Where(g => g > 0)
            .Distinct()
            .OrderByDescending(g => g)
            .Take(3)
            .ToList();

        AddTier(snapshot, "crowd_age_gap", withGuesses, positiveGaps, s => Math.Round(s.ChronoAge!.Value - s.CrowdAge!.Value, 2));

        // --- Lowest Crowd Age: top-3 distinct crowd ages (asc) ---
        var lowestAges = withGuesses
            .Select(s => Math.Round(s.CrowdAge!.Value, 2))
            .Distinct()
            .OrderBy(a => a)
            .Take(3)
            .ToList();

        AddTier(snapshot, "crowd_lowest_age", withGuesses, lowestAges, s => Math.Round(s.CrowdAge!.Value, 2));

        // local helper for tiered (gold/silver/bronze) assignment
        static void AddTier<T>(
            Dictionary<string, Dictionary<string, List<string>>> snap,
            string badgeCode,
            List<AthleteStats> pool,
            List<T> top3Values,
            Func<AthleteStats, T> selector)
            where T : notnull, IEquatable<T>
        {
            if (top3Values.Count == 0) return;
            string[] tiers = { "gold", "silver", "bronze" };
            for (int i = 0; i < top3Values.Count && i < tiers.Length; i++)
            {
                var v = top3Values[i];
                var slugs = pool
                    .Where(s => selector(s).Equals(v))
                    .OrderBy(s => s.Name, StringComparer.Ordinal)
                    .Select(s => s.Slug)
                    .ToList();

                if (slugs.Count > 0)
                    Add(snap, badgeCode, tiers[i], slugs); // BadgeCode stays the same, ScopeKey is the tier
            }
        }
    }

    public void Dispose()
    {
        _sqlite.Close();
        _sqlite.Dispose();
        GC.SuppressFinalize(this);
    }
}
