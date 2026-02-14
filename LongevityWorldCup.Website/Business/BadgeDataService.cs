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

        AddLeagueRankingAwards(stats, awards);
        AddDomainAwards(stats, awards);
        AddSubmissionAwards(stats, awards);
        AddPhenoDiffAwards(stats, awards);
        AddCrowdAwards(stats, awards);
        AddEditorialAwards(stats, awards);

        AddSeasonFinalResultsAwards(awards);

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
            if (s.LowestBortzAge.HasValue) o["LowestBortzAge"] = s.LowestBortzAge.Value;
            else o.Remove("LowestBortzAge");
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
            if (s.BortzAgeDiffFromBaseline.HasValue) o["BortzAgeDiffFromBaseline"] = s.BortzAgeDiffFromBaseline.Value;
            else o.Remove("BortzAgeDiffFromBaseline");
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
                        DefinitionHash = r.IsDBNull(5) ? null : r.GetString(5),
                        OccurredAtUtc = null
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

        var beforeByAb = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in before)
        {
            if (!x.Place.HasValue) continue;
            var ab = AbKey(x.AthleteSlug, x.BadgeLabel, x.LeagueCategory, x.LeagueValue);
            if (beforeByAb.TryGetValue(ab, out var cur))
                beforeByAb[ab] = Math.Min(cur, x.Place.Value);
            else
                beforeByAb[ab] = x.Place.Value;
        }

        var afterByAb = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in after)
        {
            if (!x.Place.HasValue) continue;
            var ab = AbKey(x.AthleteSlug, x.BadgeLabel, x.LeagueCategory, x.LeagueValue);
            if (afterByAb.TryGetValue(ab, out var cur))
                afterByAb[ab] = Math.Min(cur, x.Place.Value);
            else
                afterByAb[ab] = x.Place.Value;
        }

        static string AwardKey(string athleteSlug, string label, string cat, string? val, int? place)
            => $"{athleteSlug}|{label}|{cat}|{val ?? ""}|{(place.HasValue ? place.Value.ToString(CultureInfo.InvariantCulture) : "")}";

        var afterOccurredAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in after)
        {
            if (!x.OccurredAtUtc.HasValue) continue;
            var k = AwardKey(x.AthleteSlug, x.BadgeLabel, x.LeagueCategory, x.LeagueValue, x.Place);
            afterOccurredAt[k] = x.OccurredAtUtc.Value;
        }

        DateTime ResolveOccurredAt(string athleteSlug, string label, string cat, string? val, int? place)
        {
            var k = AwardKey(athleteSlug, label, cat, val, place);
            return afterOccurredAt.TryGetValue(k, out var ts) ? ts : occurredAtUtc;
        }

        var changedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in beforeMap.Keys) changedKeys.Add(k);
        foreach (var k in afterMap.Keys) changedKeys.Add(k);

        var items = new List<BadgeEventItem>();

        foreach (var key in changedKeys)
        {
            beforeMap.TryGetValue(key, out var prevSet);
            afterMap.TryGetValue(key, out var nextSet);

            prevSet ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            nextSet ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (prevSet.SetEquals(nextSet)) continue;

            var parts = key.Split('|');
            var label = parts.Length > 0 ? parts[0] : "";
            var cat = parts.Length > 1 ? parts[1] : "Global";
            var valStr = parts.Length > 2 ? parts[2] : null;

            int? place = null;
            if (parts.Length > 3 && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var placeParsed))
                place = placeParsed;

            var adds = nextSet.Except(prevSet, StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList();
            var removes = prevSet.Except(nextSet, StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList();

            List<string>? trueRemoves = null;
            if (removes.Count > 0)
            {
                trueRemoves = new List<string>(removes.Count);
                for (int i = 0; i < removes.Count; i++)
                {
                    var slug = removes[i];
                    var ab = AbKey(slug, label, cat, valStr);

                    if (!beforeByAb.TryGetValue(ab, out var beforeBest))
                    {
                        trueRemoves.Add(slug);
                        continue;
                    }

                    if (!afterByAb.TryGetValue(ab, out var afterBest) || afterBest > beforeBest)
                        trueRemoves.Add(slug);
                }

                if (trueRemoves.Count == 0) trueRemoves = null;
            }

            if (adds.Count == 0)
            {
                if (removes.Count == 0) continue;

                string? winner = null;
                if (nextSet.Count == 1) winner = nextSet.First();

                if (winner is not null && prevSet.Count > 1 && nextSet.Count == 1)
                {
                    items.Add(new BadgeEventItem
                    {
                        AthleteSlug = winner,
                        OccurredAtUtc = ResolveOccurredAt(winner, label, cat, string.IsNullOrEmpty(valStr) ? null : valStr, place),
                        BadgeLabel = label,
                        LeagueCategory = cat,
                        LeagueValue = string.IsNullOrEmpty(valStr) ? null : valStr,
                        Place = place,
                        BecameSoloOwner = true,
                        ReplacedSlug = null,
                        ReplacedSlugs = trueRemoves
                    });
                }

                continue;
            }

            if (place.HasValue)
            {
                var addsForward = adds.Where(slug =>
                {
                    var ab = AbKey(slug, label, cat, valStr);
                    var afterPlace = afterByAb.TryGetValue(ab, out var ap) ? (int?)ap : null;
                    var beforePlace = beforeByAb.TryGetValue(ab, out var bp) ? (int?)bp : null;
                    return afterPlace.HasValue && (!beforePlace.HasValue || afterPlace.Value < beforePlace.Value);
                }).OrderBy(s => s, StringComparer.Ordinal).ToList();

                if (addsForward.Count > 0)
                {
                    string? replaced = trueRemoves is not null && trueRemoves.Count == 1 ? trueRemoves[0] : null;
                    IReadOnlyList<string>? replacedSlugs = trueRemoves is not null && trueRemoves.Count > 1 ? trueRemoves : null;

                    for (int i = 0; i < addsForward.Count; i++)
                    {
                        var ab = AbKey(addsForward[i], label, cat, valStr);
                        var afterPlace = afterByAb.TryGetValue(ab, out var ap) ? (int?)ap : null;

                        items.Add(new BadgeEventItem
                        {
                            AthleteSlug = addsForward[i],
                            OccurredAtUtc = ResolveOccurredAt(addsForward[i], label, cat, string.IsNullOrEmpty(valStr) ? null : valStr, afterPlace),
                            BadgeLabel = label,
                            LeagueCategory = cat,
                            LeagueValue = string.IsNullOrEmpty(valStr) ? null : valStr,
                            Place = afterPlace,
                            BecameSoloOwner = false,
                            ReplacedSlug = replaced,
                            ReplacedSlugs = replacedSlugs
                        });
                    }
                }

                continue;
            }

            {
                var singleSwap =
                    prevSet.Count == 1 &&
                    nextSet.Count == 1 &&
                    adds.Count == 1 &&
                    removes.Count == 1 &&
                    !string.Equals(adds.First(), removes.First(), StringComparison.OrdinalIgnoreCase);

                IReadOnlyList<string>? replacedSlugs = null;
                if (!singleSwap && nextSet.Count == 1 && adds.Count == 1 && removes.Count > 1)
                    replacedSlugs = removes;

                for (int i = 0; i < adds.Count; i++)
                {
                    var replaced = singleSwap ? removes[0] : null;
                    items.Add(new BadgeEventItem
                    {
                        AthleteSlug = adds[i],
                        OccurredAtUtc = ResolveOccurredAt(adds[i], label, cat, string.IsNullOrEmpty(valStr) ? null : valStr, place),
                        BadgeLabel = label,
                        LeagueCategory = cat,
                        LeagueValue = string.IsNullOrEmpty(valStr) ? null : valStr,
                        Place = null,
                        BecameSoloOwner = false,
                        ReplacedSlug = replaced,
                        ReplacedSlugs = replacedSlugs
                    });
                }
            }
        }

        if (items.Count > 0)
            _events.CreateBadgeAwardEvents(
                items.Select(i => (i.AthleteSlug, i.OccurredAtUtc, i.BadgeLabel, i.LeagueCategory, i.LeagueValue, i.Place, i.BecameSoloOwner, i.ReplacedSlug, i.ReplacedSlugs)),
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

    private void AddSeasonFinalResultsAwards(List<AwardRow> awards)
    {
        try
        {
            _db.Run(sqlite =>
            {
                var seasons = new Dictionary<int, (string ClockId, string MaxFinalizedAtUtc)>();

                using (var cmd = sqlite.CreateCommand())
                {
                    cmd.CommandText = "SELECT SeasonId, ClockId, MAX(FinalizedAtUtc) FROM SeasonFinalResults GROUP BY SeasonId, ClockId;";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        var seasonId = r.GetInt32(0);
                        var clockId = r.GetString(1);
                        var maxFinalizedAtUtc = r.IsDBNull(2) ? "" : r.GetString(2);

                        if (!seasons.TryGetValue(seasonId, out var cur))
                        {
                            seasons[seasonId] = (clockId, maxFinalizedAtUtc);
                            continue;
                        }

                        var cmp = string.CompareOrdinal(maxFinalizedAtUtc, cur.MaxFinalizedAtUtc);
                        if (cmp > 0 || (cmp == 0 && string.CompareOrdinal(clockId, cur.ClockId) < 0))
                            seasons[seasonId] = (clockId, maxFinalizedAtUtc);
                    }
                }

                var ruleHashBySeason = new Dictionary<int, string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var kv in seasons)
                {
                    var seasonId = kv.Key;
                    var clockId = kv.Value.ClockId;

                    var label = BuildSeasonLabel(seasonId);

                    if (!ruleHashBySeason.TryGetValue(seasonId, out var ruleHash))
                    {
                        ruleHash = BuildSeasonFinalRuleHash(label, seasonId, topN: 20);
                        ruleHashBySeason[seasonId] = ruleHash;
                    }

                    using var cmd = sqlite.CreateCommand();
                    cmd.CommandText = "SELECT AthleteSlug, Place, SeasonClosesAtUtc FROM SeasonFinalResults WHERE SeasonId = $seasonId AND ClockId = $clockId AND Place <= 20;";
                    cmd.Parameters.AddWithValue("$seasonId", seasonId);
                    cmd.Parameters.AddWithValue("$clockId", clockId);
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        var athleteSlug = r.GetString(0);
                        var place = r.GetInt32(1);
                        var closesAtUtc = r.IsDBNull(2) ? (DateTime?)null : ParseUtc(r.GetString(2));

                        var k = $"{seasonId}|{place}|{athleteSlug}";
                        if (!seen.Add(k)) continue;

                        awards.Add(new AwardRow
                        {
                            BadgeLabel = label,
                            LeagueCategory = "Global",
                            LeagueValue = null,
                            Place = place,
                            AthleteSlug = athleteSlug,
                            DefinitionHash = ruleHash,
                            OccurredAtUtc = closesAtUtc
                        });
                    }
                }
            });
        }
        catch
        {
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
        public DateTime? OccurredAtUtc { get; init; }
    }

    class BadgeEventItem
    {
        public required string AthleteSlug { get; init; }
        public required DateTime OccurredAtUtc { get; init; }
        public required string BadgeLabel { get; init; }
        public required string LeagueCategory { get; init; }
        public string? LeagueValue { get; init; }
        public int? Place { get; init; }
        public bool BecameSoloOwner { get; init; }
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
        public double? LowestBortzAge { get; init; }
        public double? AgeReduction { get; init; }
        public double? BortzAgeReduction { get; init; }
        public int SubmissionCount { get; init; }
        public int BortzSubmissionCount { get; init; }
        public string? Division { get; init; }
        public string? Generation { get; init; }
        public string? Exclusive { get; init; }
        public double[]? BestMarkerValues { get; init; }
        public double[]? BestBortzValues { get; init; }
        public double? PhenoAgeDiffFromBaseline { get; init; }
        public double? BortzAgeDiffFromBaseline { get; init; }
        public double? CrowdAge { get; init; }
        public int CrowdCount { get; init; }
    }

    private enum BadgeScope
    {
        Global,
        Division,
        Generation,
        Exclusive,
        Amateur
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

    private static string BuildImprovementRuleHash(string label, string metricKey)
    {
        var sig = $"label={label}|category=Global|metric={metricKey}|agg=min|requires_subs>=2|ties=allowed|place=1";
        return ComputeRuleHash(sig);
    }

    private static string BuildCrowdRuleHash(string label, string metricKey, string order, bool distinctValues, int decimals)
    {
        var sig = $"label={label}|category=Global|metric={metricKey}|order={order}|distinct_values={(distinctValues ? "yes" : "no")}|round={decimals}dp|tiers=top3|ties=allowed";
        return ComputeRuleHash(sig);
    }

    private static string BuildSeasonLabel(int seasonId)
    {
        return "S" + (seasonId % 100).ToString("00", CultureInfo.InvariantCulture);
    }

    private static DateTime ParseUtc(string raw)
    {
        var dt = DateTime.Parse(raw, null, DateTimeStyles.RoundtripKind);
        return dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }


    private static string BuildSeasonFinalRuleHash(string label, int seasonId, int topN)
    {
        var sig = $"label={label}|category=Global|type=season_final|seasonId={seasonId}|source=SeasonFinalResults|topN={topN}";
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
                Label: "Bortz Age – Lowest",
                Scope: BadgeScope.Global,
                TopN: 3,
                Eligibility: a => a.LowestBortzAge.HasValue,
                Metric: a => a.LowestBortzAge,
                SortDirection: SortDir.Asc,
                TieBreaker: compTie,
                MetricKey: "BortzAge"),
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
                LowestBortzAge = r.LowestBortzAge,
                AgeReduction = r.AgeReduction,
                BortzAgeReduction = r.BortzAgeReduction,
                SubmissionCount = r.SubmissionCount,
                BortzSubmissionCount = r.BortzSubmissionCount,
                Division = r.Division,
                Generation = r.Generation,
                Exclusive = r.Exclusive,
                BestMarkerValues = r.BestMarkerValues,
                BestBortzValues = r.BestBortzValues,
                PhenoAgeDiffFromBaseline = r.PhenoAgeDiffFromBaseline,
                BortzAgeDiffFromBaseline = r.BortzAgeDiffFromBaseline,
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

    private static void AddLeagueRankingAwards(
        Dictionary<string, AthleteStats> stats,
        List<AwardRow> awards)
    {
        AddLeagueTop3("Global", null, stats.Values, awards, includeAmateursOnly: false);

        foreach (var g in stats.Values.Where(s => !string.IsNullOrWhiteSpace(s.Division)).GroupBy(s => s.Division!))
            AddLeagueTop3("Division", g.Key, g, awards, includeAmateursOnly: false);

        foreach (var g in stats.Values.Where(s => !string.IsNullOrWhiteSpace(s.Generation)).GroupBy(s => s.Generation!))
            AddLeagueTop3("Generation", g.Key, g, awards, includeAmateursOnly: false);

        foreach (var g in stats.Values.Where(s => !string.IsNullOrWhiteSpace(s.Exclusive)).GroupBy(s => s.Exclusive!))
            AddLeagueTop3("Exclusive", g.Key, g, awards, includeAmateursOnly: false);

        AddLeagueTop3("Amateur", "Amateur", stats.Values, awards, includeAmateursOnly: true);
    }

    private static void AddLeagueTop3(
        string category,
        string? value,
        IEnumerable<AthleteStats> source,
        List<AwardRow> awards,
        bool includeAmateursOnly)
    {
        var ranked = source
            .Where(s => includeAmateursOnly
                ? !s.BortzAgeReduction.HasValue && s.AgeReduction.HasValue
                : s.BortzAgeReduction.HasValue || s.AgeReduction.HasValue)
            .OrderBy(s => !s.BortzAgeReduction.HasValue) // pro (Bortz) first
            .ThenBy(s => s.BortzAgeReduction ?? s.AgeReduction ?? double.PositiveInfinity)
            .ThenBy(s => s.DobUtc ?? DateTime.MaxValue)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ThenBy(s => s.Slug, StringComparer.Ordinal)
            .Take(3)
            .ToList();

        if (ranked.Count == 0) return;

        var hash = BuildRankedRuleHash(
            label: "Age Reduction",
            leagueCategory: category,
            metricKey: includeAmateursOnly ? "LeaderboardRank_PhenoOnly" : "LeaderboardRank",
            topN: 3,
            sort: SortDir.Asc,
            tieBreakKey: "bortz_first_then_reduction_then_dob_then_name",
            generationBandsVersion: category == "Generation" ? "gen-v1" : null);

        for (int i = 0; i < ranked.Count; i++)
        {
            awards.Add(new AwardRow
            {
                BadgeLabel = "Age Reduction",
                LeagueCategory = category,
                LeagueValue = value,
                Place = i + 1,
                AthleteSlug = ranked[i].Slug,
                DefinitionHash = hash
            });
        }
    }

    private static void AddDomainAwards(
        Dictionary<string, AthleteStats> stats,
        List<AwardRow> awards)
    {
        var bortzDomains = new[]
        {
            (Label: "Best Domain – Liver", Key: "liver", Indices: new[] { 1, 16, 2, 9 }),
            (Label: "Best Domain – Kidney", Key: "kidney", Indices: new[] { 3, 5, 6 }),
            (Label: "Best Domain – Metabolic", Key: "metabolic", Indices: new[] { 19, 7, 4, 21 }),
            (Label: "Best Domain – Immune", Key: "immune", Indices: new[] { 15, 14, 13, 10, 11, 20, 12 }),
            (Label: "Best Domain – Inflammation", Key: "inflammation", Indices: new[] { 8 }),
            (Label: "Best Domain – Vitamin D", Key: "vitamin_d", Indices: new[] { 17, 18 }),
        };

        foreach (var (label, key, indices) in bortzDomains)
        {
            var candidates = stats.Values
                .Where(s => s.BestBortzValues is not null && s.BestBortzValues.Length == BortzAgeHelper.Features.Length)
                .Select(s => (s.Slug, Score: ComputeBortzDomainContribution(s.BestBortzValues!, indices)))
                .Where(x => x.Score.HasValue)
                .Select(x => (x.Slug, Score: x.Score!.Value))
                .ToList();
            AddBestDomainHolders(candidates, label, key, awards);
        }
    }

    private static void AddBestDomainHolders(
        List<(string Slug, double Score)> candidates,
        string label,
        string key,
        List<AwardRow> awards)
    {
        if (candidates.Count == 0) return;
        var best = candidates.Min(x => x.Score);
        var ruleHash = BuildDomainRuleHash(label, key);
        foreach (var x in candidates.Where(x => Math.Abs(x.Score - best) < 1e-9))
        {
            awards.Add(new AwardRow
            {
                BadgeLabel = label,
                LeagueCategory = "Global",
                LeagueValue = null,
                Place = 1,
                AthleteSlug = x.Slug,
                DefinitionHash = ruleHash
            });
        }
    }

    private static double? ComputeBortzDomainContribution(double[] values, int[] indices)
    {
        if (values.Length != BortzAgeHelper.Features.Length) return null;

        // Keep aligned with profile/radar domain contribution logic.
        var excluded = new HashSet<int> { 3, 4, 5, 16, 17 };
        double sum = 0;
        for (int i = 0; i < indices.Length; i++)
        {
            var idx = indices[i];
            if (idx < 0 || idx >= BortzAgeHelper.Features.Length) return null;
            if (excluded.Contains(idx)) continue;

            var f = BortzAgeHelper.Features[idx];
            var x = values[idx];
            if (!double.IsFinite(x)) return null;

            if (f.IsLog)
            {
                if (x <= 0) return null;
                x = Math.Log(x);
            }

            if (f.Cap.HasValue)
            {
                if (f.CapMode == BortzAgeHelper.CapMode.Floor) x = Math.Max(x, f.Cap.Value);
                else if (f.CapMode == BortzAgeHelper.CapMode.Ceiling) x = Math.Min(x, f.Cap.Value);
            }

            sum += (x - f.Mean) * f.BaaCoeff;
        }

        return sum * 10d;
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
        var phenoCandidates = stats.Values
            .Where(s => s.PhenoAgeDiffFromBaseline.HasValue && s.SubmissionCount >= 2)
            .Select(s => new { s.Slug, Diff = s.PhenoAgeDiffFromBaseline!.Value })
            .ToList();

        if (phenoCandidates.Count > 0)
        {
            var best = phenoCandidates.Min(x => x.Diff);
            var ruleHash = BuildImprovementRuleHash("PhenoAge Best Improvement", "pheno_last_minus_baseline");
            foreach (var x in phenoCandidates.Where(x => x.Diff == best))
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

        var bortzCandidates = stats.Values
            .Where(s => s.BortzAgeDiffFromBaseline.HasValue && s.BortzSubmissionCount >= 2)
            .Select(s => new { s.Slug, Diff = s.BortzAgeDiffFromBaseline!.Value })
            .ToList();
        if (bortzCandidates.Count == 0) return;

        var bortzBest = bortzCandidates.Min(x => x.Diff);
        var bortzRuleHash = BuildImprovementRuleHash("Bortz Age Best Improvement", "bortz_last_minus_baseline");
        foreach (var x in bortzCandidates.Where(x => x.Diff == bortzBest))
        {
            awards.Add(new AwardRow
            {
                BadgeLabel = "Bortz Age Best Improvement",
                LeagueCategory = "Global",
                LeagueValue = null,
                Place = 1,
                AthleteSlug = x.Slug,
                DefinitionHash = bortzRuleHash
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
