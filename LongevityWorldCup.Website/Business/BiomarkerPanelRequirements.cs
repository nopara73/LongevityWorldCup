namespace LongevityWorldCup.Website.Business;

internal static class BiomarkerPanelRequirements
{
    public static bool HasCompletePhenoPanel(BiomarkerData? biomarker)
    {
        return biomarker is not null
            && HasFiniteValue(biomarker.AlbGL)
            && HasFiniteValue(biomarker.CreatUmolL)
            && HasFiniteValue(biomarker.GluMmolL)
            && HasFiniteValue(biomarker.CrpMgL)
            && HasFiniteValue(biomarker.Wbc1000cellsuL)
            && HasFiniteValue(biomarker.LymPc)
            && HasFiniteValue(biomarker.McvFL)
            && HasFiniteValue(biomarker.RdwPc)
            && HasFiniteValue(biomarker.AlpUL);
    }

    public static bool HasCompleteBortzPanel(BiomarkerData? biomarker)
    {
        if (!HasCompletePhenoPanel(biomarker))
            return false;

        var row = biomarker!;
        return HasFiniteValue(row.UreaMmolL)
            && HasFiniteValue(row.CholesterolMmolL)
            && HasFiniteValue(row.CystatinCMgL)
            && HasFiniteValue(row.Hba1cMmolMol)
            && HasFiniteValue(row.GgtUL)
            && HasFiniteValue(row.Rbc10e12L)
            && HasFiniteValue(row.MonocytePc)
            && HasFiniteValue(row.NeutrophilPc)
            && HasFiniteValue(row.AltUL)
            && HasFiniteValue(row.ShbgNmolL)
            && HasFiniteValue(row.VitaminDNmolL)
            && HasFiniteValue(row.MchPg)
            && HasFiniteValue(row.ApoA1GL);
    }

    public static bool HasAnyBortzOnlyValue(BiomarkerData? biomarker)
    {
        if (biomarker is null)
            return false;

        return biomarker.UreaMmolL.HasValue
            || biomarker.CholesterolMmolL.HasValue
            || biomarker.CystatinCMgL.HasValue
            || biomarker.Hba1cMmolMol.HasValue
            || biomarker.GgtUL.HasValue
            || biomarker.Rbc10e12L.HasValue
            || biomarker.MonocytePc.HasValue
            || biomarker.NeutrophilPc.HasValue
            || biomarker.AltUL.HasValue
            || biomarker.ShbgNmolL.HasValue
            || biomarker.VitaminDNmolL.HasValue
            || biomarker.MchPg.HasValue
            || biomarker.ApoA1GL.HasValue;
    }

    private static bool HasFiniteValue(double? value)
        => value.HasValue && double.IsFinite(value.Value);
}
