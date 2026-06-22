using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BioageStoredBiomarkerTests
{
    [Theory]
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void BioagePages_ReadStoredBiomarkersDefensively(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));

        Assert.Contains("function readStoredBiomarkerData()", html);
        Assert.Contains("const raw = getSessionItem('biomarkerData');", html);
        Assert.Contains("if (!raw) return null;", html);
        Assert.Contains("const parsed = JSON.parse(raw);", html);
        Assert.Contains("if (parsed && typeof parsed === 'object' && !Array.isArray(parsed) && Array.isArray(parsed.Biomarkers))", html);
        Assert.Contains("return parsed;", html);
        Assert.Contains("} catch (_) {", html);
        Assert.Contains("function clearStoredBiomarkerHandoff()", html);
        Assert.Contains("removeSessionItem('biomarkerData');", html);
        Assert.Contains("removeSessionItem('chronoPhenoDifference');", html);
        Assert.Contains("removeSessionItem('chronoBortzDifference');", html);
        Assert.Contains("clearStoredBiomarkerHandoff();", html);
        Assert.Contains("return null;", html);
        Assert.Contains("const stored = readStoredBiomarkerData();", html);
        Assert.DoesNotContain("const stored = JSON.parse(sessionStorage.getItem('biomarkerData'));", html);
    }

    [Fact]
    public void PhenoPage_GuardsStorageHandoffBeforeRedirecting()
    {
        var html = File.ReadAllText(GetPagePath("pheno-age.html"));

        Assert.Contains("function storePhenoResultForNextStep(biomarkerData, chronoPhenoDifference)", html);
        Assert.Contains("sessionStorage.setItem('chronoPhenoDifference', chronoPhenoDifference.toFixed(2));", html);
        Assert.Contains("sessionStorage.setItem('biomarkerData', JSON.stringify(biomarkerData));", html);
        Assert.Contains("sessionStorage.setItem(PENDING_PAYMENT_OFFER_KEY, JSON.stringify(", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", html);
        Assert.Contains("if (!storePhenoResultForNextStep(biomarkerData, chronoPhenoDifference)) return;", html);

        var failureBody = GetStoreFailureBody(html, "function storePhenoResultForNextStep");
        Assert.Contains("clearStoredBiomarkerHandoff();", failureBody);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", failureBody);
    }

    [Fact]
    public void PhenoPage_PersistsAndRestoresWizardStep()
    {
        var html = File.ReadAllText(GetPagePath("pheno-age.html"));

        Assert.Contains("function lwcValidateStep1(silent)", html);
        Assert.Contains("if (!silent) customAlert('Please select your birth year.');", html);
        Assert.Contains("try { sessionStorage.setItem('lwcStep', '2'); } catch (e) {}", html);
        Assert.Contains("function activateDot1() { lwcSetStep(1); try { sessionStorage.setItem('lwcStep', '1'); } catch (e) {} }", html);
        Assert.Contains("function activateDot2() { if (lwcValidateStep1()) { lwcSetStep(2); try { sessionStorage.setItem('lwcStep', '2'); } catch (e) {} } }", html);
        Assert.Contains("if (getSessionItem('lwcStep') === '2' && lwcValidateStep1(true))", html);
        Assert.Contains("let restoredStep = false;", html);
        Assert.Contains("if (!restoredStep) {", html);
        Assert.Contains("try { sessionStorage.setItem('lwcStep', '1'); } catch (e) {}", html);
    }

    [Theory]
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void BioagePages_SuppressDateAlertsDuringWizardStepRestore(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));

        Assert.Contains("function getValidatedBloodDrawDate(silent)", html);
        Assert.Contains("if (!silent) {", html);
        Assert.Contains("customAlert('Please enter the date when your blood was drawn.');", html);
        Assert.Contains("customAlert('Blood draw date cannot be in the future.');", html);
        Assert.Contains("const bd = getValidatedBloodDrawDate(silent);", html);
        Assert.Contains("if (getSessionItem('lwcStep') === '2' && lwcValidateStep1(true))", html);
    }

    [Theory]
    [InlineData("pheno-age.html", "function storePhenoResultForNextStep")]
    [InlineData("bortz-age.html", "function storeBortzResultForNextStep")]
    public void BioagePages_ResetWizardStepBeforeHandoff(string fileName, string storeFunctionMarker)
    {
        var html = File.ReadAllText(GetPagePath(fileName));
        var storeStart = html.IndexOf(storeFunctionMarker, StringComparison.Ordinal);
        var storeEnd = html.IndexOf("return true;", storeStart, StringComparison.Ordinal);

        Assert.True(storeStart >= 0);
        Assert.True(storeEnd > storeStart);

        var storeBody = html[storeStart..storeEnd];

        Assert.Contains("sessionStorage.setItem('biomarkerData', JSON.stringify(biomarkerData));", storeBody);
        Assert.Contains("try { sessionStorage.setItem('lwcStep', '1'); } catch (e) {}", storeBody);
    }

    [Theory]
    [InlineData("pheno-age.html", "syncFilledBiomarkersForNewApplication(['wbc', 'lymphocyte', 'mcv', 'rcdw', 'albumin', 'ap', 'creatinine', 'glucose', 'crp']);")]
    [InlineData("bortz-age.html", "syncFilledBiomarkersForNewApplication([...bortzFieldIds, 'wbc']);")]
    public void BioagePages_CaptureFilledNewApplicationFieldsBeforeCalculationAndHandoff(string fileName, string syncCall)
    {
        var html = File.ReadAllText(GetPagePath(fileName));

        Assert.Contains("function syncFilledBiomarkersForNewApplication(fieldIds)", html);
        Assert.Contains("if (isUpdate) return;", html);
        Assert.Contains("if (el && el.value.trim() !== '') touchedBiomarkers.add(id);", html);

        var calculateStart = html.IndexOf("function calculateResult()", StringComparison.Ordinal);
        var updateGuardStart = html.IndexOf("if (isUpdate)", calculateStart, StringComparison.Ordinal);
        var proceedStart = html.IndexOf("function proceedToNextPage()", StringComparison.Ordinal);
        var handoffGuardStart = html.IndexOf("if (isUpdate && touchedBiomarkers.size === 0)", proceedStart, StringComparison.Ordinal);

        Assert.True(calculateStart >= 0);
        Assert.True(updateGuardStart > calculateStart);
        Assert.True(proceedStart >= 0);
        Assert.True(handoffGuardStart > proceedStart);

        var calculateBodyStart = html.IndexOf(syncCall, calculateStart, StringComparison.Ordinal);
        var proceedBodyStart = html.IndexOf(syncCall, proceedStart, StringComparison.Ordinal);

        Assert.True(calculateBodyStart > calculateStart);
        Assert.True(calculateBodyStart < updateGuardStart);
        Assert.True(proceedBodyStart > proceedStart);
        Assert.True(proceedBodyStart < handoffGuardStart);
    }

    [Theory]
    [InlineData("pheno-age.html", "if (touchedBiomarkers.size < 9)", "customAlert('😱 Need to submit all 9 biomarkers!');")]
    [InlineData("bortz-age.html", "if (touchedBiomarkers.size < requiredCount)", "customAlert(`😱 Need to submit all ${requiredCount} biomarkers!`);")]
    public void BioageUpdateCalculations_AllowPartialBiomarkerUpdates(string fileName, string oldGuard, string oldAlert)
    {
        var html = File.ReadAllText(GetPagePath(fileName));
        var calculateBody = GetFunctionBody(html, "function calculateResult()", "const bloodDrawDate = getValidatedBloodDrawDate();");

        Assert.Contains("if (isUpdate && athlete?.Biomarkers?.length)", calculateBody);
        Assert.Contains("if (isUpdate && touchedBiomarkers.size === 0)", calculateBody);
        Assert.Contains("customAlert('😱 No new biomarker data entered! Cannot proceed!');", calculateBody);
        Assert.DoesNotContain(oldGuard, calculateBody);
        Assert.DoesNotContain(oldAlert, calculateBody);
    }

    [Theory]
    [InlineData("pheno-age.html", "phenoAgeForm")]
    [InlineData("bortz-age.html", "bortzAgeForm")]
    public void BioagePages_ReportRequiredFieldValidityBeforeCalculating(string fileName, string formId)
    {
        var html = File.ReadAllText(GetPagePath(fileName));
        var formStart = html.IndexOf($"const form = document.getElementById('{formId}');", StringComparison.Ordinal);

        Assert.True(formStart >= 0);

        var submitStart = html.IndexOf("form.addEventListener('submit', function (e)", formStart, StringComparison.Ordinal);

        Assert.True(submitStart > formStart);

        var calculateIndex = html.IndexOf("calculateResult();", submitStart, StringComparison.Ordinal);
        var validityIndex = html.IndexOf("if (!this.reportValidity())", submitStart, StringComparison.Ordinal);

        Assert.True(validityIndex > submitStart);
        Assert.True(calculateIndex > submitStart);
        Assert.True(validityIndex < calculateIndex);
    }

    [Theory]
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void BioagePages_AllowManualBloodDrawDateEntry(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));
        var dateInputStart = html.IndexOf("id=\"blood-draw-date\"", StringComparison.Ordinal);

        Assert.True(dateInputStart >= 0);

        var dateInputEnd = html.IndexOf('>', dateInputStart);

        Assert.True(dateInputEnd > dateInputStart);

        var dateInputHtml = html[dateInputStart..dateInputEnd];

        Assert.DoesNotContain("inputmode=\"none\"", dateInputHtml);
        Assert.DoesNotContain("onkeydown=\"return false\"", dateInputHtml);
        Assert.DoesNotContain("onkeypress=\"return false\"", dateInputHtml);
        Assert.DoesNotContain("onkeyup=\"return false\"", dateInputHtml);
        Assert.DoesNotContain("onpaste=\"return false\"", dateInputHtml);
        Assert.DoesNotContain("ondrop=\"return false\"", dateInputHtml);
        Assert.Contains("bloodDrawDateInput.addEventListener('focus', lwcOpenDatePickerGuarded);", html);
        Assert.Contains("bloodDrawDateInput.addEventListener('click', lwcOpenDatePickerGuarded);", html);
        Assert.DoesNotContain("Block manual edits", html);
        Assert.DoesNotContain("bloodDrawDateInput.addEventListener('beforeinput'", html);
    }

    [Theory]
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void BioagePages_ReplaceMalformedPendingPaymentOfferBeforeHandoff(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));

        Assert.Contains("function hasUsablePendingPaymentOffer()", html);
        Assert.Contains("const rawOffer = getSessionItem(PENDING_PAYMENT_OFFER_KEY);", html);
        Assert.Contains("const parsedOffer = JSON.parse(rawOffer);", html);
        Assert.Contains("if (isUsablePaymentOffer(parsedOffer)) return true;", html);
        Assert.Contains("function isUsablePaymentOffer(paymentOffer)", html);
        Assert.Contains("typeof paymentOffer.source === 'string'", html);
        Assert.Contains("typeof paymentOffer.offerType === 'string'", html);
        Assert.Contains("typeof paymentOffer.currency === 'string'", html);
        Assert.Contains("typeof paymentOffer.amountUsd === 'number'", html);
        Assert.Contains("Number.isFinite(paymentOffer.amountUsd)", html);
        Assert.Contains("paymentOffer.amountUsd >= 0", html);
        Assert.Contains("function clearPendingPaymentOffer()", html);
        Assert.Contains("removeSessionItem(PENDING_PAYMENT_OFFER_KEY);", html);
        Assert.Contains("clearPendingPaymentOffer();", html);
        Assert.Contains("if (!isUpdate && !hasUsablePendingPaymentOffer())", html);
        Assert.DoesNotContain("if (!isUpdate && !sessionStorage.getItem(PENDING_PAYMENT_OFFER_KEY))", html);
    }

    [Fact]
    public void BortzPage_GuardsStorageHandoffBeforeRedirecting()
    {
        var html = File.ReadAllText(GetPagePath("bortz-age.html"));

        Assert.Contains("function storeBortzResultForNextStep(biomarkerData, chronoBortzDifference, chronoPhenoDifference)", html);
        Assert.Contains("sessionStorage.setItem('chronoBortzDifference', chronoBortzDifference.toFixed(2));", html);
        Assert.Contains("sessionStorage.setItem('chronoPhenoDifference', chronoPhenoDifference.toFixed(2));", html);
        Assert.Contains("sessionStorage.setItem('biomarkerData', JSON.stringify(biomarkerData));", html);
        Assert.Contains("sessionStorage.setItem(PENDING_PAYMENT_OFFER_KEY, JSON.stringify(", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", html);
        Assert.Contains("if (!storeBortzResultForNextStep(biomarkerData, chronoBortzDifference, chronoPhenoDifference)) return;", html);

        var failureBody = GetStoreFailureBody(html, "function storeBortzResultForNextStep");
        Assert.Contains("clearStoredBiomarkerHandoff();", failureBody);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", failureBody);
    }

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
    public void PhenoPage_AppliesCorrectableUnitsBeforeHandoff()
    {
        var html = File.ReadAllText(GetPagePath("pheno-age.html"));

        Assert.Contains("Creatinine value suggests µmol/L. Correcting the unit.", html);
        Assert.Contains("setUnit('creatinineUnit', 1);", html);
        Assert.Contains("Creatinine value suggests mg/dL. Correcting the unit.", html);
        Assert.Contains("setUnit('creatinineUnit', 0.0113);", html);
        Assert.Contains("Glucose value suggests mg/dL. Correcting the unit.", html);
        Assert.Contains("setUnit('glucoseUnit', 18.016);", html);
        Assert.Contains("Glucose value suggests mmol/L. Correcting the unit.", html);
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

    private static string GetPagePath(string fileName)
    {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "onboarding", fileName);
    }

    private static string GetStoreFailureBody(string html, string storeFunctionMarker)
    {
        var storeStart = html.IndexOf(storeFunctionMarker, StringComparison.Ordinal);
        var catchStart = html.IndexOf("} catch (_) {", storeStart, StringComparison.Ordinal);
        var returnFalse = html.IndexOf("return false;", catchStart, StringComparison.Ordinal);

        Assert.True(storeStart >= 0);
        Assert.True(catchStart > storeStart);
        Assert.True(returnFalse > catchStart);

        return html[catchStart..returnFalse];
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
