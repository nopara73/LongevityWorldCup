using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BioageStoredBiomarkerTests
{
    [Fact]
    public void BortzPage_CorrectsApoA1MgDlEnteredWithGLUnit()
    {
        var html = File.ReadAllText(GetPagePath("bortz-age.html"));

        Assert.Contains("const apoa1El = document.getElementById('apoa1');", html);
        Assert.Contains("const apoa1UnitEl = document.getElementById('apoa1Unit');", html);
        Assert.Contains("if (!isNaN(v) && u === 1 && v > 10)", html);
        Assert.Contains("setUnit(apoa1UnitEl, 100);", html);

        var proceedBody = GetFunctionBody(html, "function proceedToNextPage()", "const biomarkerData = {");
        var correctionIndex = proceedBody.IndexOf("correctCorrectableUnits();", StringComparison.Ordinal);
        var storeIndex = proceedBody.IndexOf("store('apoa1', 'ApoA1GL'", StringComparison.Ordinal);

        Assert.True(correctionIndex >= 0);
        Assert.True(storeIndex > correctionIndex);
    }

    [Fact]
    public void BortzPage_CorrectsHba1cPercentEnteredWithMmolMolUnit()
    {
        var html = File.ReadAllText(GetPagePath("bortz-age.html"));

        Assert.Contains("const hba1cEl = document.getElementById('hba1c');", html);
        Assert.Contains("const hba1cUnitEl = document.getElementById('hba1cUnit');", html);
        Assert.Contains("if (!isNaN(v) && !isPct && !isEAG && u === 1 && v > 0 && v < 15)", html);
        Assert.Contains("setUnit(hba1cUnitEl, 0.0915);", html);

        var proceedBody = GetFunctionBody(html, "function proceedToNextPage()", "const biomarkerData = {");
        var correctionIndex = proceedBody.IndexOf("correctCorrectableUnits();", StringComparison.Ordinal);
        var storeIndex = proceedBody.IndexOf("store('hba1c', 'Hba1cMmolMol'", StringComparison.Ordinal);

        Assert.True(correctionIndex >= 0);
        Assert.True(storeIndex > correctionIndex);
    }

    [Fact]
    public void PhenoPage_AppliesCorrectableUnitsBeforeHandoff()
    {
        var html = File.ReadAllText(GetPagePath("pheno-age.html"));

        Assert.Contains("Creatinine value suggests µmol/L. Correcting the unit.", html);
        Assert.Contains("creatinineUnit === 0.0113 && creatinineValue > 20", html);
        Assert.Contains("setUnit('creatinineUnit', 1);", html);
        Assert.Contains("Creatinine value suggests mg/dL. Correcting the unit.", html);
        Assert.Contains("setUnit('creatinineUnit', 0.0113);", html);
        Assert.Contains("Glucose value suggests mg/dL. Correcting the unit.", html);
        Assert.Contains("setUnit('glucoseUnit', 18.016);", html);
        Assert.Contains("Glucose value suggests mmol/L. Correcting the unit.", html);
        Assert.Contains("glucoseUnit === 18.016 && glucoseValue < 30 && glucoseValue > 0", html);
        Assert.Contains("setUnit('glucoseUnit', 1);", html);

        var proceedBody = GetFunctionBody(html, "function proceedToNextPage()", "const biomarkerData = {");
        var correctionIndex = proceedBody.IndexOf("correctCorrectableUnits();", StringComparison.Ordinal);
        var albuminStoreIndex = proceedBody.IndexOf("entry.AlbGL = parseFloat", StringComparison.Ordinal);
        var creatinineStoreIndex = proceedBody.IndexOf("entry.CreatUmolL = parseFloat", StringComparison.Ordinal);
        var glucoseStoreIndex = proceedBody.IndexOf("entry.GluMmolL = parseFloat", StringComparison.Ordinal);

        Assert.True(correctionIndex >= 0);
        Assert.True(albuminStoreIndex > correctionIndex);
        Assert.True(creatinineStoreIndex > correctionIndex);
        Assert.True(glucoseStoreIndex > correctionIndex);
    }

    [Fact]
    public void BortzPage_CorrectsNormalMmolLGlucoseEnteredWithMgDlUnit()
    {
        var html = File.ReadAllText(GetPagePath("bortz-age.html"));

        Assert.Contains("Glucose value suggests mmol/L. Correcting the unit.", html);
        Assert.Contains("u === 18.016 && v < 30 && v > 0", html);
        Assert.Contains("setUnit(gluUnitEl, 1);", html);

        var proceedBody = GetFunctionBody(html, "function proceedToNextPage()", "const biomarkerData = {");
        var correctionIndex = proceedBody.IndexOf("correctCorrectableUnits();", StringComparison.Ordinal);
        var glucoseStoreIndex = proceedBody.IndexOf("store('glucose', 'GluMmolL'", StringComparison.Ordinal);

        Assert.True(correctionIndex >= 0);
        Assert.True(glucoseStoreIndex > correctionIndex);
    }

    [Fact]
    public void BortzPage_CorrectsNormalUmolLCreatinineEnteredWithMgDlUnit()
    {
        var html = File.ReadAllText(GetPagePath("bortz-age.html"));

        Assert.Contains("Creatinine value suggests µmol/L. Correcting the unit.", html);
        Assert.Contains("u === 0.0113 && v > 20", html);
        Assert.Contains("setUnit(creatUnitEl, 1);", html);

        var proceedBody = GetFunctionBody(html, "function proceedToNextPage()", "const biomarkerData = {");
        var correctionIndex = proceedBody.IndexOf("correctCorrectableUnits();", StringComparison.Ordinal);
        var creatinineStoreIndex = proceedBody.IndexOf("store('creatinine', 'CreatUmolL'", StringComparison.Ordinal);

        Assert.True(correctionIndex >= 0);
        Assert.True(creatinineStoreIndex > correctionIndex);
    }

    [Fact]
    public void BortzPage_CorrectsCommonSiBortzOnlyValuesEnteredWithUsDefaultUnits()
    {
        var html = File.ReadAllText(GetPagePath("bortz-age.html"));

        Assert.Contains("u === 2.801 && v < 7.5 && v > 0", html);
        Assert.Contains("Urea value suggests mmol/L. Correcting the unit.", html);
        Assert.Contains("u === 0.1 && v > 0.3 && v < 3", html);
        Assert.Contains("Cystatin C value suggests mg/L. Correcting the unit.", html);
        Assert.Contains("u === 100 && v < 10 && v > 0", html);
        Assert.Contains("ApoA1 value suggests g/L. Correcting the unit.", html);
        Assert.Contains("u === 0.0347 && v >= 10 && v < 200", html);
        Assert.Contains("SHBG value suggests nmol/L. Correcting the unit.", html);

        var proceedBody = GetFunctionBody(html, "function proceedToNextPage()", "const biomarkerData = {");
        var correctionIndex = proceedBody.IndexOf("correctCorrectableUnits();", StringComparison.Ordinal);
        var ureaStoreIndex = proceedBody.IndexOf("store('urea', 'UreaMmolL'", StringComparison.Ordinal);
        var cystatinStoreIndex = proceedBody.IndexOf("store('cystatin_c', 'CystatinCMgL'", StringComparison.Ordinal);
        var apoa1StoreIndex = proceedBody.IndexOf("store('apoa1', 'ApoA1GL'", StringComparison.Ordinal);
        var shbgStoreIndex = proceedBody.IndexOf("store('shbg', 'ShbgNmolL'", StringComparison.Ordinal);

        Assert.True(correctionIndex >= 0);
        Assert.True(ureaStoreIndex > correctionIndex);
        Assert.True(cystatinStoreIndex > correctionIndex);
        Assert.True(apoa1StoreIndex > correctionIndex);
        Assert.True(shbgStoreIndex > correctionIndex);
    }

    private static string GetPagePath(string fileName)
    {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "onboarding", fileName);
    }

    private static string GetFunctionBody(string html, string functionMarker, string endMarker)
    {
        var functionStart = html.IndexOf(functionMarker, StringComparison.Ordinal);
        var functionEnd = html.IndexOf(endMarker, functionStart, StringComparison.Ordinal);

        Assert.True(functionStart >= 0);
        Assert.True(functionEnd > functionStart);

        return html[functionStart..functionEnd];
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var startDirectory = Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory;
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LongevityWorldCup.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from {startDirectory}.");
    }
}
