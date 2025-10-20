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
    BadgeId        TEXT NOT NULL,
    ScopeKey       TEXT NOT NULL,
    HoldersJson    TEXT NOT NULL,
    DefinitionHash TEXT NULL,
    UpdatedAt      TEXT NOT NULL,
    PRIMARY KEY (BadgeId, ScopeKey)
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

        // Compute: BadgeId -> ScopeKey -> List<slug>
        var snapshot = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
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

        // Persist (replace all rows)
        var defHash = ComputeDefinitionHash(defs);
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
INSERT INTO {TableName} (BadgeId, ScopeKey, HoldersJson, DefinitionHash, UpdatedAt)
VALUES (@b, @s, @h, @dh, @u);";
            var pB = ins.Parameters.Add("@b", SqliteType.Text);
            var pS = ins.Parameters.Add("@s", SqliteType.Text);
            var pH = ins.Parameters.Add("@h", SqliteType.Text);
            var pD = ins.Parameters.Add("@dh", SqliteType.Text);
            var pU = ins.Parameters.Add("@u", SqliteType.Text);

            foreach (var (badgeId, scopes) in snapshot)
            {
                foreach (var (scopeKey, holders) in scopes)
                {
                    pB.Value = badgeId;
                    pS.Value = scopeKey;
                    pH.Value = JsonSerializer.Serialize(holders);
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
        string badgeId, string scopeKey, IEnumerable<string> slugs)
    {
        if (!map.TryGetValue(badgeId, out var scopes))
        {
            scopes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            map[badgeId] = scopes;
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
        public double? ChronoAge { get; init; }          // years
        public double? LowestPhenoAge { get; init; }     // years
        public double? AgeReduction { get; init; }       // pheno - chrono (lower is better)
        public int SubmissionCount { get; init; }
        public string? Division { get; init; }
        public string? Generation { get; init; }
        public string? Exclusive { get; init; }
    }

    private enum BadgeScope { Global, Division, Generation, Exclusive }
    private enum SortDir { Asc, Desc }

    private sealed record BadgeDefinition(
        string Id,
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
            // Mirrors badges.js: top3 oldest chronologically. :contentReference[oaicite:1]{index=1}
            new BadgeDefinition(
                Id: "oldest_global",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.ChronoAge.HasValue,
                Metric: a => a.ChronoAge,
                SortDirection: SortDir.Desc,
                TieBreaker: compTie),

            // top3 youngest chronologically. :contentReference[oaicite:2]{index=2}
            new BadgeDefinition(
                Id: "youngest_global",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.ChronoAge.HasValue,
                Metric: a => a.ChronoAge,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie),

            // top3 biologically youngest (lowest pheno age). :contentReference[oaicite:3]{index=3}
            new BadgeDefinition(
                Id: "bio_youngest_global",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.LowestPhenoAge.HasValue,
                Metric: a => a.LowestPhenoAge,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie),

            // Ultimate League: top3 smallest age reduction (pheno - chrono). :contentReference[oaicite:4]{index=4}
            new BadgeDefinition(
                Id: "ultimate_league_global",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.AgeReduction.HasValue,
                Metric: a => a.AgeReduction,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie),

            // Per-division top3 by competition metric (fixes the FE "slice without sort"). :contentReference[oaicite:5]{index=5}
            new BadgeDefinition(
                Id: "top3_by_division",
                Scope: BadgeScope.Division,
                TopN: 3,
                Eligibility: a => a.AgeReduction.HasValue && !string.IsNullOrWhiteSpace(a.Division),
                Metric: a => a.AgeReduction,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie),

            // Per-generation top3 by competition metric. :contentReference[oaicite:6]{index=6}
            new BadgeDefinition(
                Id: "top3_by_generation",
                Scope: BadgeScope.Generation,
                TopN: 3,
                Eligibility: a => a.AgeReduction.HasValue && !string.IsNullOrWhiteSpace(a.Generation),
                Metric: a => a.AgeReduction,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie),

            // Per-exclusive league top3 (if `Exclusive` exists). :contentReference[oaicite:7]{index=7}
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

            // Lowest pheno-age across Biomarkers (age-at-entry aware), same as ranking code. :contentReference[oaicite:8]{index=8}
            int submissionCount = 0;
            double lowestPheno = double.PositiveInfinity;

            if (o["Biomarkers"] is JsonArray biomArr)
            {
                foreach (var entry in biomArr.OfType<JsonObject>())
                {
                    submissionCount++;

                    var entryDate = asOf;
                    var ds = entry["Date"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(ds) &&
                        DateTime.TryParse(ds, null, DateTimeStyles.RoundtripKind, out var parsed))
                    {
                        entryDate = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
                    }

                    double AgeAt(DateTime d) => dob.HasValue ? (d.Date - dob.Value.Date).TotalDays / 365.2425 : double.NaN;

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

                        if (!double.IsNaN(ph) && !double.IsInfinity(ph) && ph < lowestPheno)
                            lowestPheno = ph;
                    }
                }
            }

            if (double.IsNaN(lowestPheno) || double.IsInfinity(lowestPheno))
                lowestPheno = chrono ?? double.PositiveInfinity;

            double? ageReduction = (chrono.HasValue && !double.IsInfinity(lowestPheno))
                ? Math.Round(lowestPheno - chrono.Value, 2)
                : (double?)null;

            result[slug] = new AthleteStats
            {
                Slug = slug,
                Name = name,
                DobUtc = dob,
                ChronoAge = chrono,
                LowestPhenoAge = double.IsInfinity(lowestPheno) ? null : Math.Round(lowestPheno, 2),
                AgeReduction = ageReduction,
                SubmissionCount = submissionCount,
                Division = TryGetString(o, "Division"),
                Generation = TryGetString(o, "Generation"),
                Exclusive = TryGetString(o, "ExclusiveLeague")
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

    public void Dispose()
    {
        _sqlite.Close();
        _sqlite.Dispose();
        GC.SuppressFinalize(this);
    }
}
