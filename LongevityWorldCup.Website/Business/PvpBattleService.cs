using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed record PvpBattleRound(string BiomarkerName, double ValueA, double ValueB, string? WinnerSlug);

public sealed record PvpBattleResult(
    string AthleteSlugA,
    string AthleteSlugB,
    int AgeA,
    int AgeB,
    IReadOnlyList<PvpBattleRound> Rounds,
    int? RankA,
    int? RankB,
    string? WinnerSlug);

public sealed class PvpBattleService
{
    private readonly AthleteDataService _athletes;

    public PvpBattleService(AthleteDataService athletes)
    {
        _athletes = athletes ?? throw new ArgumentNullException(nameof(athletes));
    }

    public PvpBattleResult? CreateRandomBattle(DateTime? asOfUtc, int biomarkerCount)
    {
        var asOf = (asOfUtc ?? DateTime.UtcNow).Date;
        var snapshot = _athletes.GetAthletesSnapshot();
        var statsMap = PhenoStatsCalculator.BuildAll(snapshot, asOf);
        var order = _athletes.GetRankingsOrder(asOfUtc);
        var rankBySlug = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var idx = 0;
        foreach (var o in order.OfType<JsonObject>())
        {
            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(slug))
                rankBySlug[slug] = ++idx;
        }

        var eligible = statsMap.Values
            .Where(r => r.BestMarkerValues != null && r.BestMarkerValues.Length == PhenoAgeHelper.Biomarkers.Length)
            .Select(r => r.Slug)
            .ToList();
        if (eligible.Count < 2) return null;

        var rnd = new Random();
        var i1 = rnd.Next(eligible.Count);
        var i2 = rnd.Next(eligible.Count);
        while (i2 == i1 && eligible.Count > 1)
            i2 = rnd.Next(eligible.Count);
        var slugA = eligible[i1];
        var slugB = eligible[i2];
        var valsA = statsMap[slugA].BestMarkerValues!;
        var valsB = statsMap[slugB].BestMarkerValues!;

        var startIndex = 1;
        var availableCount = PhenoAgeHelper.Biomarkers.Length - startIndex;
        var count = Math.Min(Math.Max(biomarkerCount, 1), availableCount);
        var indices = Enumerable.Range(startIndex, availableCount).OrderBy(_ => rnd.Next()).Take(count).ToList();
        var rounds = new List<PvpBattleRound>();
        var scoreA = 0;
        var scoreB = 0;
        foreach (var bi in indices)
        {
            var b = PhenoAgeHelper.Biomarkers[bi];
            var vA = valsA[bi];
            var vB = valsB[bi];
            string? winner = null;
            var lowerBetter = b.Coeff > 0;
            if (lowerBetter)
            {
                if (vA < vB) { winner = slugA; scoreA++; }
                else if (vB < vA) { winner = slugB; scoreB++; }
                else { scoreA++; scoreB++; }
            }
            else
            {
                if (vA > vB) { winner = slugA; scoreA++; }
                else if (vB > vA) { winner = slugB; scoreB++; }
                else { scoreA++; scoreB++; }
            }
            rounds.Add(new PvpBattleRound(b.Name, vA, vB, winner));
        }

        string? winnerSlug = null;
        if (scoreA > scoreB) winnerSlug = slugA;
        else if (scoreB > scoreA) winnerSlug = slugB;
        var ageA = (int)Math.Round(statsMap[slugA].ChronoAge ?? 0);
        var ageB = (int)Math.Round(statsMap[slugB].ChronoAge ?? 0);
        rankBySlug.TryGetValue(slugA, out var rankA);
        rankBySlug.TryGetValue(slugB, out var rankB);

        return new PvpBattleResult(
            AthleteSlugA: slugA,
            AthleteSlugB: slugB,
            AgeA: ageA,
            AgeB: ageB,
            Rounds: rounds,
            RankA: rankA > 0 ? rankA : null,
            RankB: rankB > 0 ? rankB : null,
            WinnerSlug: winnerSlug);
    }
}
