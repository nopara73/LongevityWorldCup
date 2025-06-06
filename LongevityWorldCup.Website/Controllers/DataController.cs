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

            foreach (var bmNode in biomarkerArray)
            {
                var bm = bmNode!.AsObject();
                double val;

                val = bm["AlbGL"]!.GetValue<double>();
                if (double.IsNaN(alb) || val > alb) alb = val;

                val = bm["CreatUmolL"]!.GetValue<double>();
                if (double.IsNaN(creat) || val < creat) creat = val;

                val = bm["GluMmolL"]!.GetValue<double>();
                if (double.IsNaN(glu) || val < glu) glu = val;

                val = bm["CrpMgL"]!.GetValue<double>();
                if (double.IsNaN(crp) || val < crp) crp = val;

                val = bm["Wbc1000cellsuL"]!.GetValue<double>();
                if (double.IsNaN(wbc) || val < wbc) wbc = val;

                val = bm["LymPc"]!.GetValue<double>();
                if (double.IsNaN(lym) || val > lym) lym = val;

                val = bm["McvFL"]!.GetValue<double>();
                if (double.IsNaN(mcv) || val < mcv) mcv = val;

                val = bm["RdwPc"]!.GetValue<double>();
                if (double.IsNaN(rdw) || val < rdw) rdw = val;

                val = bm["AlpUL"]!.GetValue<double>();
                if (double.IsNaN(alp) || val < alp) alp = val;
            }

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