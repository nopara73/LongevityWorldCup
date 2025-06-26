using System.Text.Json.Nodes;

namespace LongevityWorldCup.Website.Business
{
    public static class AgeCalculation
    {
        public static double CalculateChronologicalAge(JsonObject athlete)
        {
            var dob = athlete["DateOfBirth"]!.AsObject();
            var dt = new DateTime(dob["Year"]!.GetValue<int>(), dob["Month"]!.GetValue<int>(), dob["Day"]!.GetValue<int>(), 0, 0, 0, DateTimeKind.Utc);
            var today = DateTime.UtcNow.Date;
            var days = (today - dt).TotalDays;
            return Math.Round(days / 365.2425, 1);
        }

        public static double CalculateLowestPhenoAge(JsonObject athlete)
        {
            var biomarkers = athlete["Biomarkers"]?.AsArray();
            if (biomarkers is null || biomarkers.Count == 0)
                return 0;
            var best = new double[10];
            bool first = true;
            foreach (var b in biomarkers)
            {
                if (b is not JsonObject obj)
                    continue;

                if (!TryGetDouble(obj, "AlbGL", out double AlbGL) ||
                    !TryGetDouble(obj, "CreatUmolL", out double Creat) ||
                    !TryGetDouble(obj, "GluMmolL", out double Glu) ||
                    !TryGetDouble(obj, "CrpMgL", out double Crp) ||
                    !TryGetDouble(obj, "Wbc1000cellsuL", out double Wbc) ||
                    !TryGetDouble(obj, "LymPc", out double Lym) ||
                    !TryGetDouble(obj, "McvFL", out double Mcv) ||
                    !TryGetDouble(obj, "RdwPc", out double Rdw) ||
                    !TryGetDouble(obj, "AlpUL", out double Alp))
                {
                    continue; // skip incomplete biomarker sets
                }

                if (first)
                {
                    best[1] = AlbGL;
                    best[2] = Creat;
                    best[3] = Glu;
                    best[4] = Crp;
                    best[5] = Wbc;
                    best[6] = Lym;
                    best[7] = Mcv;
                    best[8] = Rdw;
                    best[9] = Alp;
                    first = false;
                }
                else
                {
                    best[1] = Math.Max(best[1], AlbGL);
                    best[2] = Math.Min(best[2], Creat);
                    best[3] = Math.Min(best[3], Glu);
                    best[4] = Math.Min(best[4], Crp);
                    best[5] = Math.Min(best[5], Wbc);
                    best[6] = Math.Max(best[6], Lym);
                    best[7] = Math.Min(best[7], Mcv);
                    best[8] = Math.Min(best[8], Rdw);
                    best[9] = Math.Min(best[9], Alp);
                }
            }
            if (first)
                return 0;
            best[0] = CalculateChronologicalAge(athlete);
            return CalculatePhenoAge(best);
        }

        // Implementation of PhenoAge calculation
        public static double CalculatePhenoAge(double[] best)
        {
            double age = best[0];
            double albumin = best[1];
            double creat = best[2];
            double glu = best[3];
            double crp = best[4];
            double wbc = best[5];
            double lym = best[6];
            double mcv = best[7];
            double rcdw = best[8];
            double ap = best[9];

            double score = 0;
            score += age * 0.0804;
            score += albumin * -0.0336;
            score += Math.Max(creat, 44) * 0.0095;
            score += Math.Max(glu, 4.44) * 0.1953;
            score += crp * 0.0954;
            score += Math.Max(wbc, 3.5) * 0.0554;
            score += Math.Min(lym, 60) * -0.012;
            score += mcv * 0.0268;
            score += Math.Max(rcdw, 11.4) * 0.3306;
            score += ap * 0.0019;

            // constants from the published formula
            double xb = score - 19.907;
            double mort = 1 - Math.Exp(-Math.Exp(xb) * 0.0076927);
            double pheno = 141.50 + Math.Log(-0.00553 * Math.Log(1 - mort)) / 0.090165;
            return Math.Round(pheno, 1);
        }

        private static bool TryGetDouble(JsonObject obj, string key, out double value)
        {
            value = 0;
            if (obj[key] is JsonNode node && double.TryParse(node.ToString(), out var result))
            {
                value = result;
                return true;
            }
            return false;
        }
    }
}
