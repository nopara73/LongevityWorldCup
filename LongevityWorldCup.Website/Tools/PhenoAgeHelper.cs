using System.Globalization;

namespace LongevityWorldCup.Website.Tools
{
    public static class PhenoAgeHelper
    {
        public enum CapMode { None, Floor, Ceiling }
        public sealed record Biomarker(string Id, string Name, double Coeff, double? Cap = null, CapMode Mode = CapMode.None);

        public static readonly Biomarker[] Biomarkers =
        {
            new("age","Age",0.0804),
            new("albumin","Albumin",-0.0336),
            new("creatinine","Creatinine",0.0095,44,CapMode.Floor),
            new("glucose","Glucose",0.1953,4.44,CapMode.Floor),
            new("crp","C-reactive protein",0.0954),
            new("wbc","White blood cell count",0.0554,3.5,CapMode.Floor),
            new("lymphocyte","Lymphocytes",-0.012,60,CapMode.Ceiling),
            new("mcv","Mean corpuscular volume",0.0268),
            new("rcdw","Red cell distribution width",0.3306,11.4,CapMode.Floor),
            new("ap","Alkaline phosphatase",0.0019)
        };

        private static double ApplyCap(double value, Biomarker bm) =>
            bm.Mode switch
            {
                CapMode.Floor => Math.Max(value, bm.Cap!.Value),
                CapMode.Ceiling => Math.Min(value, bm.Cap!.Value),
                _ => value
            };

        public static double ParseInput(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return double.NaN;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        }

        public static double CalculatePhenoAgeFromRaw(
            double ageYears,
            double albuminGL,
            double creatUmolL,
            double gluMmolL,
            double crpMgL,
            double wbc_1000cells_uL,
            double lymphPc,
            double mcvFL,
            double rdwPc,
            double alpUL)
        {
            if (crpMgL <= 0) return double.NaN;
            var lnCrpOver10 = Math.Log(crpMgL / 10.0);
            var values = new[]
            {
                ageYears, albuminGL, creatUmolL, gluMmolL, lnCrpOver10,
                wbc_1000cells_uL, lymphPc, mcvFL, rdwPc, alpUL
            };
            return CalculatePhenoAge(values);
        }

        public static double CalculateAgeFromDOBAndBloodDrawDate(DateTime birthDate, DateTime bloodDrawDate)
        {
            if (birthDate > bloodDrawDate) throw new ArgumentException("Date of birth cannot be in the future.");
            var utc1 = new DateTime(birthDate.Year, birthDate.Month, birthDate.Day, 0, 0, 0, DateTimeKind.Utc);
            var utc2 = new DateTime(bloodDrawDate.Year, bloodDrawDate.Month, bloodDrawDate.Day, 0, 0, 0, DateTimeKind.Utc);
            var totalDays = (utc2 - utc1).TotalDays;
            return Math.Round(totalDays / 365.2425, 2);
        }

        public static double CalculateLiverScore(double[] markerValues)
        {
            var albumin = markerValues[1];
            var ap = markerValues[9];
            var coeffAlbumin = Biomarkers[1].Coeff;
            var coeffAP = Biomarkers[9].Coeff;
            return albumin * coeffAlbumin + ap * coeffAP;
        }

        public static double CalculateKidneyScore(double[] markerValues)
        {
            var creatinine = ApplyCap(markerValues[2], Biomarkers[2]);
            return creatinine * Biomarkers[2].Coeff;
        }

        public static double CalculateMetabolicScore(double[] markerValues)
        {
            var glucose = ApplyCap(markerValues[3], Biomarkers[3]);
            return glucose * Biomarkers[3].Coeff;
        }

        public static double CalculateInflammationScore(double[] markerValues)
        {
            var crp = markerValues[4];
            return crp * Biomarkers[4].Coeff;
        }

        public static double CalculateImmuneScore(double[] markerValues)
        {
            var wbc = ApplyCap(markerValues[5], Biomarkers[5]);
            var lymphocyte = ApplyCap(markerValues[6], Biomarkers[6]);
            var mcv = markerValues[7];
            var rcdw = ApplyCap(markerValues[8], Biomarkers[8]);

            return wbc * Biomarkers[5].Coeff
                 + lymphocyte * Biomarkers[6].Coeff
                 + mcv * Biomarkers[7].Coeff
                 + rcdw * Biomarkers[8].Coeff;
        }

        public static double CalculatePhenoAge(double[] markerValues)
        {
            var ageScore = markerValues[0] * Biomarkers[0].Coeff;

            var totalScore =
                ageScore +
                CalculateLiverScore(markerValues) +
                CalculateKidneyScore(markerValues) +
                CalculateMetabolicScore(markerValues) +
                CalculateInflammationScore(markerValues) +
                CalculateImmuneScore(markerValues);

            const double b0 = -19.9067;
            const double gamma = 0.0076927;
            var rollingTotal = totalScore + b0;

            const int tmonths = 120;
            var mortalityScore = 1 - Math.Exp(-Math.Exp(rollingTotal) * (Math.Exp(gamma * tmonths) - 1) / gamma);

            return 141.50225 + Math.Log(-0.00553 * Math.Log(1 - mortalityScore)) / 0.090165;
        }

        private const double ScalingFactor = 1 / 0.090165;

        public static double CalculateLiverPhenoAgeContributor(double[] markerValues) =>
            CalculateLiverScore(markerValues) * ScalingFactor;

        public static double CalculateKidneyPhenoAgeContributor(double[] markerValues) =>
            CalculateKidneyScore(markerValues) * ScalingFactor;

        public static double CalculateMetabolicPhenoAgeContributor(double[] markerValues) =>
            CalculateMetabolicScore(markerValues) * ScalingFactor;

        public static double CalculateInflammationPhenoAgeContributor(double[] markerValues) =>
            CalculateInflammationScore(markerValues) * ScalingFactor;

        public static double CalculateImmunePhenoAgeContributor(double[] markerValues) =>
            CalculateImmuneScore(markerValues) * ScalingFactor;
    }
}
