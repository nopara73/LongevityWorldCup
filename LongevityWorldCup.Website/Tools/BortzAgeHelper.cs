using System.Globalization;

namespace LongevityWorldCup.Website.Tools
{
    /// <summary>
    /// Bortz Age / Biological Age Acceleration (BAA) from Bortz et al. 2023
    /// https://www.nature.com/articles/s42003-023-05456-z
    /// Coefficients and Scottish test-set means from https://github.com/bortzjd/bloodmarker_BA_estimation
    /// BAA = 10 * sum((x_i - mean_i) * baaCoeff_i). Biological age = chronological age + BAA.
    /// Excludes MSCV, PDW, PCT, and reticulocytes per product requirement.
    /// </summary>
    public static class BortzAgeHelper
    {
        public enum CapMode { None, Floor, Ceiling }

        /// <summary>Feature id, display name, mean (for centering), BAA coefficient, log transform, and optional cap (PhenoAge-style).</summary>
        public sealed record BortzFeature(string Id, string Name, double Mean, double BaaCoeff, bool IsLog = false, double? Cap = null, CapMode CapMode = CapMode.None);

        /// <summary>Order matches the model. Age uses (ENET - SexAge) coefficient; sex is not in BAA. Caps match PhenoAge where applicable (no creatinine cap; Bortz would need ceiling, not floor).</summary>
        public static readonly BortzFeature[] Features =
        {
            new("age", "Age", 56.0487752, 0.074763266 - 0.100432393),
            new("albumin", "Albumin", 45.1238763, -0.011331946),
            new("alp", "Alkaline phosphatase", 82.6847975, 0.00164946),
            new("urea", "Urea", 5.3547152, -0.029554872),
            new("cholesterol", "Total Cholesterol", 5.6177437, -0.0805656),
            new("creatinine", "Creatinine", 71.565605, -0.01095746), // no cap: PhenoAge has floor, Bortz would need ceiling
            new("cystatin_c", "Cystatin C", 0.900946, 1.859556436),
            new("hba1c", "Hemoglobin A1c (HbA1c)", 35.4785711, 0.018116675),
            new("crp", "C-Reactive Protein (CRP)", 0.3003624, 0.079109916, IsLog: true),
            new("ggt", "Gamma-Glutamyl Transferase (GGT)", 3.3795613, 0.265550311, IsLog: true),
            new("rbc", "Red blood cell count", 4.4994648, -0.204442153),
            new("mcv", "Mean corpuscular volume", 91.9251099, 0.017165356),
            new("rdw", "Red cell distribution width", 13.4342296, 0.202009895, Cap: 11.4, CapMode: CapMode.Floor),
            new("monocyte_count", "Monocytes", 0.4746987, 0.36937314),
            new("neutrophil_count", "Neutrophils", 4.1849454, 0.06679092),
            new("lymphocyte_percentage", "Lymphocytes (%)", 28.5817604, -0.0108158, Cap: 60, CapMode: CapMode.Ceiling),
            new("alt", "Alanine aminotransferase", 3.077868, -0.312442261, IsLog: true),
            new("shbg", "Sex Hormone-Binding Globulin (SHBG)", 3.8202787, 0.292323186, IsLog: true),
            new("vitamin_d", "Vitamin D (25-OH)", 3.6052878, -0.265467867, IsLog: true),
            new("glucose", "Glucose", 4.9563054, 0.032171478, Cap: 4.44, CapMode: CapMode.Floor),
            new("mch", "Mean corpuscular hemoglobin", 31.8396206, 0.02746487),
            new("apoa1", "Apolipoprotein A1 (ApoA1)", 1.5238771, -0.185139395),
        };

        private static double ApplyCap(double value, BortzFeature f) =>
            f.CapMode switch
            {
                CapMode.Floor => Math.Max(value, f.Cap!.Value),
                CapMode.Ceiling => Math.Min(value, f.Cap!.Value),
                _ => value
            };

        public static double ParseInput(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return double.NaN;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        }

        /// <summary>
        /// Derive neutrophil count (10^9/L) from WBC and neutrophil %. Matches frontend: we store only %; count = WBC * (Pc/100).
        /// </summary>
        public static double DeriveNeutrophilCountFromPc(double wbc1000CellsPerMicroL, double neutrophilPc) =>
            wbc1000CellsPerMicroL * (neutrophilPc / 100.0);

        /// <summary>
        /// Derive monocyte count (10^9/L) from WBC and monocyte %. Matches frontend: we store only %; count = WBC * (Pc/100).
        /// </summary>
        public static double DeriveMonocyteCountFromPc(double wbc1000CellsPerMicroL, double monocytePc) =>
            wbc1000CellsPerMicroL * (monocytePc / 100.0);

        /// <summary>
        /// Compute Biological Age Acceleration (BAA). Values must be in model order and units:
        /// age (years), albumin (g/L), ALP (U/L), urea (mmol/L), cholesterol (mmol/L), creatinine (µmol/L),
        /// cystatin_c (mg/L), HbA1c (mmol/mol), CRP raw (mg/L), GGT raw (U/L), RBC (10^12/L), MCV (fL), RDW (%),
        /// monocytes (10^9/L), neutrophils (10^9/L), lymphocyte % (%), ALT raw (U/L), SHBG raw (nmol/L), Vitamin D raw (nmol/L),
        /// glucose (mmol/L), MCH (pg), ApoA1 (g/L).
        /// When building the raw array from biomarker JSON, use DeriveMonocyteCountFromPc/DeriveNeutrophilCountFromPc when NeutrophilPc/MonocytePc are present (WBC in 10^9/L = Wbc1000cellsuL).
        /// CRP, GGT, ALT, SHBG, Vitamin D are log-transformed internally.
        /// </summary>
        public static double CalculateBAA(double[] values)
        {
            if (values == null || values.Length != Features.Length)
                return double.NaN;

            double sum = 0;
            for (int i = 0; i < Features.Length; i++)
            {
                var f = Features[i];
                double x = values[i];
                if (f.IsLog && x > 0)
                    x = Math.Log(x);
                else if (f.IsLog && x <= 0)
                    return double.NaN;
                x = ApplyCap(x, f);
                double centered = x - f.Mean;
                sum += centered * f.BaaCoeff;
            }
            return sum * 10;
        }

        /// <summary>Biological age = chronological age + BAA.</summary>
        public static double CalculateBortzAge(double chronologicalAgeYears, double baa)
        {
            if (!double.IsFinite(chronologicalAgeYears) || !double.IsFinite(baa))
                return double.NaN;
            return Math.Max(0, chronologicalAgeYears + baa);
        }

        /// <summary>Single entry point: compute BAA from raw values then return biological age.</summary>
        public static double CalculateBortzAgeFromRaw(double chronologicalAgeYears, double[] rawValuesInFeatureOrder)
        {
            var baa = CalculateBAA(rawValuesInFeatureOrder);
            if (!double.IsFinite(baa)) return double.NaN;
            return CalculateBortzAge(chronologicalAgeYears, baa);
        }
    }
}
