using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Nodes;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DataController(AthleteDataService svc) : Controller
    {
        private readonly AthleteDataService _svc = svc;

        [HttpGet("flags")]
        public IActionResult GetFlags()
        {
            return Ok(Flags.Flag);
        }

        [HttpGet("divisions")]
        public IActionResult GetDivisions()
        {
            return Ok(Divisions.Division);
        }

        [HttpGet("athletes")]
        public IActionResult GetAthletes() => Ok(_svc.Athletes);

        [HttpGet("leaderboard-rank")]
        public IActionResult GetLeaderboardRank(double phenoAgeDifference)
        {
            var reductions = new List<double>();
            foreach (var node in _svc.Athletes)
            {
                if (node is not JsonObject athlete)
                    continue;

                var reduction = CalculateAgeReduction(athlete);
                if (!double.IsNaN(reduction))
                    reductions.Add(reduction);
            }

            reductions.Sort();
            int total = reductions.Count;

            int rank = reductions.TakeWhile(r => r < phenoAgeDifference).Count() + 1;
            double percentile = 100d * (total - rank + 1) / total;

            return Ok(new { rank, totalParticipants = total, percentile });
        }

        private static double CalculateAgeReduction(JsonObject athlete)
        {
            var dobObj = athlete["DateOfBirth"]!.AsObject();
            int year = dobObj["Year"]!.GetValue<int>();
            int month = dobObj["Month"]!.GetValue<int>();
            int day = dobObj["Day"]!.GetValue<int>();
            var dob = new DateTime(year, month, day);

            double chronologicalAge = PhenoAgeCalculator.CalculateAgeFromDob(dob);

            var biomarkerArray = athlete["Biomarkers"]!.AsArray();

            double alb = double.NaN;
            double creat = double.NaN;
            double glu = double.NaN;
            double crp = double.NaN;
            double wbc = double.NaN;
            double lym = double.NaN;
            double mcv = double.NaN;
            double rdw = double.NaN;
            double alp = double.NaN;

            bool anyCompleteSet = false;
            foreach (var bmNode in biomarkerArray)
            {
                var bm = bmNode!.AsObject();

                if (
                    bm.TryGetPropertyValue("AlbGL", out var albNode) &&
                    bm.TryGetPropertyValue("CreatUmolL", out var creatNode) &&
                    bm.TryGetPropertyValue("GluMmolL", out var gluNode) &&
                    bm.TryGetPropertyValue("CrpMgL", out var crpNode) &&
                    bm.TryGetPropertyValue("Wbc1000cellsuL", out var wbcNode) &&
                    bm.TryGetPropertyValue("LymPc", out var lymNode) &&
                    bm.TryGetPropertyValue("McvFL", out var mcvNode) &&
                    bm.TryGetPropertyValue("RdwPc", out var rdwNode) &&
                    bm.TryGetPropertyValue("AlpUL", out var alpNode))
                {
                    anyCompleteSet = true;

                    double val;

                    val = albNode!.GetValue<double>();
                    if (double.IsNaN(alb) || val > alb) alb = val;

                    val = creatNode!.GetValue<double>();
                    if (double.IsNaN(creat) || val < creat) creat = val;

                    val = gluNode!.GetValue<double>();
                    if (double.IsNaN(glu) || val < glu) glu = val;

                    val = crpNode!.GetValue<double>();
                    if (double.IsNaN(crp) || val < crp) crp = val;

                    val = wbcNode!.GetValue<double>();
                    if (double.IsNaN(wbc) || val < wbc) wbc = val;

                    val = lymNode!.GetValue<double>();
                    if (double.IsNaN(lym) || val > lym) lym = val;

                    val = mcvNode!.GetValue<double>();
                    if (double.IsNaN(mcv) || val < mcv) mcv = val;

                    val = rdwNode!.GetValue<double>();
                    if (double.IsNaN(rdw) || val < rdw) rdw = val;

                    val = alpNode!.GetValue<double>();
                    if (double.IsNaN(alp) || val < alp) alp = val;
                }
            }

            if (!anyCompleteSet)
                return double.NaN;

            var values = new[]
            {
                chronologicalAge,
                alb,
                creat,
                glu,
                Math.Log(crp / 10.0),
                wbc,
                lym,
                mcv,
                rdw,
                alp
            };

            double phenoAge = PhenoAgeCalculator.CalculatePhenoAge(values);
            return phenoAge - chronologicalAge;
        }
    }
}