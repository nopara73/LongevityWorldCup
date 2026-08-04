using System.Globalization;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

internal static class SubmittedAgeDifferenceCalculator
{
    internal readonly record struct Result(double? PhenoDifference, double? BortzDifference);

    internal static Result Calculate(
        DateOfBirthData? dateOfBirth,
        IReadOnlyList<BiomarkerData>? biomarkers,
        bool includePheno,
        bool includeBortz)
    {
        var biomarker = biomarkers?.LastOrDefault();
        if (dateOfBirth is null || biomarker is null || string.IsNullOrWhiteSpace(biomarker.Date))
            return default;

        DateTime birthDate;
        try
        {
            birthDate = new DateTime(
                dateOfBirth.Year,
                dateOfBirth.Month,
                dateOfBirth.Day,
                0,
                0,
                0,
                DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return default;
        }

        if (!DateOnly.TryParseExact(
                biomarker.Date.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var resultDate))
        {
            return default;
        }

        var resultDateUtc = DateTime.SpecifyKind(resultDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var chronologicalAge = (resultDateUtc - birthDate.Date).TotalDays / 365.2425;
        if (!double.IsFinite(chronologicalAge) || chronologicalAge < 0)
            return default;

        var phenoDifference = includePheno
            ? CalculatePhenoDifference(chronologicalAge, biomarker)
            : null;
        var bortzDifference = includeBortz
            ? CalculateBortzDifference(chronologicalAge, biomarker)
            : null;

        return new Result(phenoDifference, bortzDifference);
    }

    private static double? CalculatePhenoDifference(double chronologicalAge, BiomarkerData biomarker)
    {
        if (!TryGetFinite(biomarker.AlbGL, out var albumin)
            || !TryGetFinite(biomarker.CreatUmolL, out var creatinine)
            || !TryGetFinite(biomarker.GluMmolL, out var glucose)
            || !TryGetFinite(biomarker.CrpMgL, out var crp)
            || !TryGetFinite(biomarker.Wbc1000cellsuL, out var wbc)
            || !TryGetFinite(biomarker.LymPc, out var lymphocytes)
            || !TryGetFinite(biomarker.McvFL, out var mcv)
            || !TryGetFinite(biomarker.RdwPc, out var rdw)
            || !TryGetFinite(biomarker.AlpUL, out var alp)
            || crp <= 0)
        {
            return null;
        }

        var biologicalAge = PhenoAgeHelper.CalculatePhenoAgeFromRaw(
            chronologicalAge,
            albumin,
            creatinine,
            glucose,
            crp,
            wbc,
            lymphocytes,
            mcv,
            rdw,
            alp);

        return double.IsFinite(biologicalAge)
            ? biologicalAge - chronologicalAge
            : null;
    }

    private static double? CalculateBortzDifference(double chronologicalAge, BiomarkerData biomarker)
    {
        if (!TryGetFinite(biomarker.AlbGL, out var albumin)
            || !TryGetFinite(biomarker.AlpUL, out var alp)
            || !TryGetFinite(biomarker.UreaMmolL, out var urea)
            || !TryGetFinite(biomarker.CholesterolMmolL, out var cholesterol)
            || !TryGetFinite(biomarker.CreatUmolL, out var creatinine)
            || !TryGetFinite(biomarker.CystatinCMgL, out var cystatinC)
            || !TryGetFinite(biomarker.Hba1cMmolMol, out var hba1c)
            || !TryGetFinite(biomarker.CrpMgL, out var crp)
            || !TryGetFinite(biomarker.GgtUL, out var ggt)
            || !TryGetFinite(biomarker.Rbc10e12L, out var rbc)
            || !TryGetFinite(biomarker.McvFL, out var mcv)
            || !TryGetFinite(biomarker.RdwPc, out var rdw)
            || !TryGetFinite(biomarker.Wbc1000cellsuL, out var wbc)
            || !TryGetFinite(biomarker.MonocytePc, out var monocytePercentage)
            || !TryGetFinite(biomarker.NeutrophilPc, out var neutrophilPercentage)
            || !TryGetFinite(biomarker.LymPc, out var lymphocytePercentage)
            || !TryGetFinite(biomarker.AltUL, out var alt)
            || !TryGetFinite(biomarker.ShbgNmolL, out var shbg)
            || !TryGetFinite(biomarker.VitaminDNmolL, out var vitaminD)
            || !TryGetFinite(biomarker.GluMmolL, out var glucose)
            || !TryGetFinite(biomarker.MchPg, out var mch)
            || !TryGetFinite(biomarker.ApoA1GL, out var apoA1))
        {
            return null;
        }

        var values = new[]
        {
            chronologicalAge,
            albumin,
            alp,
            urea,
            cholesterol,
            creatinine,
            cystatinC,
            hba1c,
            crp,
            ggt,
            rbc,
            mcv,
            rdw,
            BortzAgeHelper.DeriveMonocyteCountFromPc(wbc, monocytePercentage),
            BortzAgeHelper.DeriveNeutrophilCountFromPc(wbc, neutrophilPercentage),
            lymphocytePercentage,
            alt,
            shbg,
            vitaminD,
            glucose,
            mch,
            apoA1
        };
        var biologicalAge = BortzAgeHelper.CalculateBortzAgeFromRaw(chronologicalAge, values);

        return double.IsFinite(biologicalAge)
            ? biologicalAge - chronologicalAge
            : null;
    }

    private static bool TryGetFinite(double? value, out double finiteValue)
    {
        finiteValue = value.GetValueOrDefault();
        return value.HasValue && double.IsFinite(finiteValue);
    }
}
