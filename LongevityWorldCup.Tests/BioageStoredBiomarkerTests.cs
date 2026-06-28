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
        var flow = File.ReadAllText(GetBioageFlowPath());

        Assert.Contains("function readStoredBiomarkerData()", html);
        Assert.Contains("const raw = getSessionItem('biomarkerData');", html);
        Assert.Contains("if (!raw) return null;", html);
        Assert.Contains("const parsed = JSON.parse(raw);", html);
        Assert.Contains("if (parsed && typeof parsed === 'object' && !Array.isArray(parsed) && Array.isArray(parsed.Biomarkers))", html);
        Assert.Contains("return parsed;", html);
        Assert.Contains("} catch (_) {", html);
        Assert.Contains("const clearStoredBiomarkerHandoff = () => bioageFlow.clearStoredBiomarkerHandoff(removeSessionItem);", html);
        Assert.Contains("function clearStoredBiomarkerHandoff(removeItem)", flow);
        Assert.Contains("remove('biomarkerData');", flow);
        Assert.Contains("remove('chronoPhenoDifference');", flow);
        Assert.Contains("remove('chronoBortzDifference');", flow);
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
        Assert.Contains("function serializePendingPaymentOffer(offer)", html);
        Assert.Contains("if (!isUsablePaymentOffer(offer)) return null;", html);
        Assert.Contains("function isUsablePaymentOffer(paymentOffer)", html);
        Assert.Contains("typeof paymentOffer.source === 'string'", html);
        Assert.Contains("typeof paymentOffer.offerType === 'string'", html);
        Assert.Contains("typeof paymentOffer.currency === 'string'", html);
        Assert.Contains("typeof paymentOffer.amountUsd === 'number'", html);
        Assert.Contains("Number.isFinite(paymentOffer.amountUsd)", html);
        Assert.Contains("paymentOffer.amountUsd >= 0", html);
        Assert.Contains("const serializedOffer = JSON.stringify(offer);", html);
        Assert.Contains("let serializedPaymentOffer = null;", html);
        Assert.Contains("try {", html);
        Assert.Contains("const adjustedPaymentOffer = window.applyPaymentAdjustmentsToPaymentOffer", html);
        Assert.Contains("serializedPaymentOffer = serializePendingPaymentOffer(adjustedPaymentOffer);", html);
        Assert.Contains("} catch (_) {", html);
        Assert.Contains("serializedPaymentOffer = null;", html);
        Assert.Contains("customAlert('Payment details could not be saved. Enable browser storage and try again.')\n                        .then(() => document.getElementById('continueButton')?.focus());", html);
        Assert.Contains("const setSessionItem = bioageFlow.setSessionItem;", html);
        Assert.Contains("serializedBiomarkerData = JSON.stringify(biomarkerData);", html);
        Assert.Contains("setSessionItem('chronoPhenoDifference', chronoPhenoDifference.toFixed(2))", html);
        Assert.Contains("setSessionItem('biomarkerData', serializedBiomarkerData)", html);
        Assert.Contains("setSessionItem('lwcStep', '1')", html);
        Assert.Contains("setSessionItem(PENDING_PAYMENT_OFFER_KEY, serializedPaymentOffer)", html);
        Assert.DoesNotContain("sessionStorage.setItem('chronoPhenoDifference', chronoPhenoDifference.toFixed(2));", html);
        Assert.DoesNotContain("sessionStorage.setItem('biomarkerData', JSON.stringify(biomarkerData));", html);
        Assert.DoesNotContain("sessionStorage.setItem(PENDING_PAYMENT_OFFER_KEY, serializedPaymentOffer);", html);
        Assert.DoesNotContain("sessionStorage.setItem(PENDING_PAYMENT_OFFER_KEY, JSON.stringify(", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.')\n                    .then(() => document.getElementById('continueButton')?.focus());", html);
        Assert.Contains("if (!storePhenoResultForNextStep(biomarkerData, chronoPhenoDifference)) return;", html);

        var failureBody = GetStoreFailureBody(html, "function storePhenoResultForNextStep");
        Assert.Contains("clearStoredBiomarkerHandoff();", failureBody);
        Assert.Contains("if (!isUpdate) clearPendingPaymentOffer();", failureBody);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.')\n                    .then(() => document.getElementById('continueButton')?.focus());", failureBody);
    }

    [Theory]
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void BioagePages_PersistAndRestoreWizardStepWithSafeStorage(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));

        Assert.Contains("function lwcValidateStep1(silent)", html);
        Assert.Contains("const yearSelect = document.getElementById('dob-year');", html);
        Assert.Contains("customAlert('Please select your birth year.')\n                        .then(() => yearSelect.focus());", html);
        Assert.Contains("setSessionItem('lwcStep', '2');", html);
        Assert.Contains("setSessionItem('lwcStep', '1');", html);
        Assert.Contains("function activateDot1() { lwcSetStep(1); setSessionItem('lwcStep', '1'); }", html);
        Assert.Contains("function activateDot2() { if (lwcValidateStep1()) { lwcSetStep(2); setSessionItem('lwcStep', '2'); } }", html);
        Assert.Contains("if (getSessionItem('lwcStep') === '2' && lwcValidateStep1(true))", html);
        Assert.Contains("let restoredStep = false;", html);
        Assert.Contains("if (!restoredStep) {", html);
        Assert.DoesNotContain("sessionStorage.setItem('lwcStep'", html);
    }

    [Theory]
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void BioagePages_SuppressDateAlertsDuringWizardStepRestore(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));

        Assert.Contains("function getValidatedBloodDrawDate(silent)", html);
        Assert.Contains("if (!silent) {", html);
        Assert.Contains("customAlert('Please enter the date when your blood was drawn.')\n                        .then(() => bloodDrawDateInput.focus());", html);
        Assert.Contains("customAlert('Blood draw date cannot be in the future.')\n                        .then(() => bloodDrawDateInput.focus());", html);
        Assert.Contains("const bd = getValidatedBloodDrawDate(silent);", html);
        Assert.Contains("if (getSessionItem('lwcStep') === '2' && lwcValidateStep1(true))", html);
    }

    [Fact]
    public void BortzPage_FocusesWbcAfterWbcValidationAlerts()
    {
        var html = File.ReadAllText(GetPagePath("bortz-age.html"));

        Assert.Contains("customAlert('Please enter a valid White Blood Cell Count (WBC).')\n                    .then(() => wbcInputEl.focus());", html);
        Assert.Contains("customAlert('Enter White Blood Cell Count (WBC) to use Lymphocytes in absolute units.').then(() => {\n                                document.getElementById('wbc')?.focus();", html);
        Assert.Contains("customAlert('Enter White Blood Cell Count (WBC) to use Neutrophils in %.').then(() => {\n                                document.getElementById('wbc')?.focus();", html);
        Assert.Contains("customAlert('Enter White Blood Cell Count (WBC) to use Monocytes in %.').then(() => {\n                                document.getElementById('wbc')?.focus();", html);
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

        Assert.Contains("setSessionItem('biomarkerData', serializedBiomarkerData)", storeBody);
        Assert.Contains("setSessionItem('lwcStep', '1')", storeBody);
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
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void BioagePages_IgnoreBlankStoredBiomarkerValuesWhenSeedingTouchedFields(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));

        Assert.Contains("function hasStoredFiniteBiomarkerValue(value)", html);
        Assert.Contains("if (value === null || value === undefined || typeof value === 'boolean') return false;", html);
        Assert.Contains("if (typeof value === 'number') return Number.isFinite(value);", html);
        Assert.Contains("const trimmed = value.trim();", html);
        Assert.Contains("return trimmed !== '' && Number.isFinite(Number(trimmed));", html);
        Assert.Contains("if (hasStoredFiniteBiomarkerValue", html);
        Assert.DoesNotContain("latest[prop] != null && !isNaN(latest[prop])", html);
        Assert.DoesNotContain("val != null && !isNaN(val)", html);
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
        Assert.Contains("customAlert('Change at least one biomarker value before continuing.')", calculateBody);
        Assert.Contains("focusFirstEditableBiomarker", calculateBody);
        Assert.Contains("function focusFirstEditableBiomarker(fieldIds)", html);
        Assert.Contains("expandFieldCard(input);", html);
        Assert.Contains("input.focus();", html);
        Assert.DoesNotContain("No new biomarker data entered", html);
        Assert.DoesNotContain("Cannot proceed", html);
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
    public void BioagePages_ShowStableDateOfBirthCalculationErrors(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));
        var calculateBody = GetFunctionBody(html, "function calculateResult()", "// Add age as the first marker value");

        Assert.Contains("chronologicalAgeAtTimeOfTest = window.calculateAgeAtDate", calculateBody);
        Assert.Contains("const yearSelect = document.getElementById('dob-year');", calculateBody);
        Assert.Contains("customAlert(\"Please select a year.\")\n                    .then(() => yearSelect.focus());", calculateBody);
        Assert.Contains("customAlert('Date of birth could not be read. Please check it and try again.')\n                    .then(() => yearSelect.focus());", calculateBody);
        Assert.Contains("if (chronologicalAgeAtTimeOfTest < 0)", calculateBody);
        Assert.Contains("customAlert('Date of birth cannot be after the blood draw date.')\n                    .then(() => yearSelect.focus());", calculateBody);
        Assert.DoesNotContain("customAlert(error.message)", calculateBody);
        Assert.DoesNotContain("throw new Error(\"Invalid date of birth", calculateBody);
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
    public void BioageUpdatePages_RenderSelectedAthleteNameAsText(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));

        Assert.Contains("document.getElementById('mainPageTitleH2').textContent = athlete.Name;", html);
        Assert.DoesNotContain("document.getElementById('mainPageTitleH2').innerHTML = athlete.Name;", html);
    }

    [Theory]
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void BioagePages_RenderResultLabelsAndUpdateInstructionsAsText(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));

        Assert.Contains("yearsTextElement.textContent = yearsDelta + (ageDiff < 0 ? `years 🚀` : `years`);", html);
        Assert.Contains("document.getElementById('mainInstructions').textContent = 'Submit your latest test results. All fields are required and must be from the same day.';", html);
        Assert.DoesNotContain("yearsTextElement.innerHTML =", html);
        Assert.DoesNotContain("document.getElementById('mainInstructions').innerHTML = 'Submit your latest test results. All fields are required and must be from the same day.';", html);
    }

    [Theory]
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void BioageUpdatePages_HideWizardNavigationAndForceBiomarkerStep(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));
        var flow = File.ReadAllText(GetBioageFlowPath());

        Assert.Contains("function hideUpdateModeStepNavigation()", html);
        Assert.Contains("bioageFlow.hideUpdateModeStepNavigation();", html);
        Assert.Contains("function hideUpdateModeStepNavigation()", flow);
        Assert.Contains("const wizardNav = document.querySelector('.lwc-wizard-nav');", flow);
        Assert.Contains("if (wizardNav) wizardNav.hidden = true;", flow);
        Assert.Contains("const stepBackButton = document.getElementById('lwcToStep1Btn');", flow);
        Assert.Contains("const stepBackActions = stepBackButton?.closest('.lwc-step-actions');", flow);
        Assert.Contains("if (stepBackActions) stepBackActions.hidden = true;", flow);
        Assert.Contains("if (isUpdate && hasSelectedAthlete) {\n                hideUpdateModeStepNavigation();\n                lwcSetStep(2);\n                setSessionItem('lwcStep', '2');\n            } else {", html);
        Assert.Contains("if (getSessionItem('lwcStep') === '2' && lwcValidateStep1(true))", html);
    }

    [Theory]
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void BioagePages_PromoteCalculateButtonWithoutSecondaryStylingConflict(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));
        var flow = File.ReadAllText(GetBioageFlowPath());
        var updateButtonBody = GetFunctionBody(html, "function updateCalculateButton()", "let isResultCalculated = false;");

        Assert.Contains("bioageFlow.updateCalculateButton();", updateButtonBody);
        Assert.Contains("function updateCalculateButton()", flow);
        Assert.Contains("calculateButton.classList.remove('green');", flow);
        Assert.Contains("calculateButton.classList.add('grey', 'flow-action--secondary');", flow);
        Assert.Contains("calculateButton.classList.remove('grey', 'flow-action--secondary');", flow);
        Assert.Contains("calculateButton.classList.add('green');", flow);
    }

    [Theory]
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void BioagePages_UseSharedUnitSpecificBiomarkerExamplePlaceholders(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));
        var flow = File.ReadAllText(GetBioageFlowPath());

        Assert.Contains("function syncBiomarkerExamplePlaceholders(root)", flow);
        Assert.Contains("function updateBiomarkerExamplePlaceholder(selectOrInput)", flow);
        Assert.Contains("select.addEventListener('change', () => updateBiomarkerExamplePlaceholder(select));", flow);
        Assert.Contains("glucose: { 'mmol/L': '5.2', 'mg/dL': '94' }", flow);
        Assert.Contains("creatinine: { 'µmol/L': '80', 'mg/dL': '0.9' }", flow);
        Assert.Contains("albumin: { 'g/L': '44', 'g/dL': '4.4' }", flow);
        Assert.Contains("hba1c: { 'mmol/mol (IFCC)': '35', '%': '5.4', 'eAG (mmol/L)': '6.0', 'eAG (mg/dL)': '108' }", flow);
        Assert.Contains("bioageFlow.syncBiomarkerExamplePlaceholders();", html);
        Assert.Contains("unitEl.value = String(val);\n                bioageFlow.updateBiomarkerExamplePlaceholder(unitEl);", html);
        Assert.Contains("bioageFlow.updateBiomarkerExamplePlaceholder(crpUnit);", html);
        Assert.DoesNotContain("window.addEventListener('pageshow', updateModeFormInitializerUnique);", html);
    }

    [Theory]
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void BioagePages_UseExplicitBackDestinationsForUpdateAndOnboardingFlows(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));
        var flow = File.ReadAllText(GetBioageFlowPath());

        Assert.Contains("onclick=\"navigateBackFromBioage()\"", html);
        Assert.Contains("function getBioageBackDestination()", html);
        Assert.Contains("return bioageFlow.getBackDestination(isUpdate);", html);
        Assert.Contains("bioageFlow.navigateBack(isUpdate);", html);
        Assert.Contains("function getBackDestination(isUpdate)", flow);
        Assert.Contains("return isUpdate ? '/dashboard' : '/join';", flow);
        Assert.Contains("window.navigateToFlowDestination(getBackDestination(isUpdate));", flow);
        Assert.DoesNotContain("onclick=\"window.goBackOrHome()\"", html);
    }

    [Theory]
    [InlineData("pheno-age.html", "phenoAgeRankPreview")]
    [InlineData("bortz-age.html", "bortzAgeRankPreview")]
    public void BioageRankPreview_WaitsForModulesDefensively(string fileName, string previewElementId)
    {
        var html = File.ReadAllText(GetPagePath(fileName));

        Assert.Contains($"if (window.LwcBioAgeRankPreview) window.LwcBioAgeRankPreview.render('{previewElementId}'", html);
        Assert.Contains("Promise.resolve(window.modulesReady).catch(() => {}).then(() => {", html);
        Assert.DoesNotContain("window.modulesReady.then(() => {", html);
    }

    [Theory]
    [InlineData("pheno-age.html", "PHENO_LAB_GEO_CACHE_KEY")]
    [InlineData("bortz-age.html", "BORTZ_LAB_GEO_CACHE_KEY")]
    public void BioagePages_UseSafeStorageForLabGeoCache(string fileName, string cacheKey)
    {
        var html = File.ReadAllText(GetPagePath(fileName));
        var flow = File.ReadAllText(GetBioageFlowPath());
        var accessGateStart = html.IndexOf("function initialize", StringComparison.Ordinal);
        var accessGateEnd = html.IndexOf("if (bloodDrawDateInput)", accessGateStart, StringComparison.Ordinal);

        Assert.True(accessGateStart >= 0);
        Assert.True(accessGateEnd > accessGateStart);

        var accessGateBody = html[accessGateStart..accessGateEnd];

        Assert.Contains("const setLocalItem = bioageFlow.setLocalItem;", html);
        Assert.Contains("const getLocalItem = bioageFlow.getLocalItem;", html);
        Assert.Contains("const removeLocalItem = bioageFlow.removeLocalItem;", html);
        Assert.Contains("function setLocalItem(key, value)", flow);
        Assert.Contains("function getLocalItem(key)", flow);
        Assert.Contains("function removeLocalItem(key)", flow);
        Assert.Contains("setBrowserStorageItem('localStorage', key, value)", flow);
        Assert.Contains("getBrowserStorageItem('localStorage', key)", flow);
        Assert.Contains("removeBrowserStorageItem('localStorage', key)", flow);
        Assert.Contains($"const raw = getLocalItem({cacheKey});", accessGateBody);
        Assert.Contains($"setLocalItem({cacheKey}, JSON.stringify({{", accessGateBody);
        Assert.Contains($"removeLocalItem({cacheKey});", accessGateBody);
        Assert.DoesNotContain("localStorage.getItem", accessGateBody);
        Assert.DoesNotContain("localStorage.setItem", accessGateBody);
        Assert.DoesNotContain("localStorage.removeItem", accessGateBody);
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
        Assert.Contains("function serializePendingPaymentOffer(offer)", html);
        Assert.Contains("if (!isUsablePaymentOffer(offer)) return null;", html);
        Assert.Contains("function isUsablePaymentOffer(paymentOffer)", html);
        Assert.Contains("typeof paymentOffer.source === 'string'", html);
        Assert.Contains("typeof paymentOffer.offerType === 'string'", html);
        Assert.Contains("typeof paymentOffer.currency === 'string'", html);
        Assert.Contains("typeof paymentOffer.amountUsd === 'number'", html);
        Assert.Contains("Number.isFinite(paymentOffer.amountUsd)", html);
        Assert.Contains("paymentOffer.amountUsd >= 0", html);
        Assert.Contains("const serializedOffer = JSON.stringify(offer);", html);
        Assert.Contains("function preserveAppliedDiscountMetadata(offer, result)", html);
        Assert.Contains("if (!hasDiscountCode || !window.addActiveDiscountMetadataToPaymentOffer) return offer;", html);
        Assert.Contains("return window.addActiveDiscountMetadataToPaymentOffer(offer);", html);
        Assert.Contains("return null;", html);
        Assert.Contains("let serializedPaymentOffer = null;", html);
        Assert.Contains("try {", html);
        Assert.Contains("const adjustedPaymentOffer = window.applyPaymentAdjustmentsToPaymentOffer", html);
        Assert.Contains("serializedPaymentOffer = serializePendingPaymentOffer(adjustedPaymentOffer);", html);
        Assert.Contains("} catch (_) {", html);
        Assert.Contains("serializedPaymentOffer = null;", html);
        Assert.Contains("customAlert('Payment details could not be saved. Enable browser storage and try again.')\n                        .then(() => document.getElementById('continueButton')?.focus());", html);
        Assert.Contains("const setSessionItem = bioageFlow.setSessionItem;", html);
        Assert.Contains("serializedBiomarkerData = JSON.stringify(biomarkerData);", html);
        Assert.Contains("setSessionItem('chronoBortzDifference', chronoBortzDifference.toFixed(2))", html);
        Assert.Contains("setSessionItem('chronoPhenoDifference', chronoPhenoDifference.toFixed(2))", html);
        Assert.Contains("setSessionItem('biomarkerData', serializedBiomarkerData)", html);
        Assert.Contains("setSessionItem('lwcStep', '1')", html);
        Assert.Contains("setSessionItem(PENDING_PAYMENT_OFFER_KEY, serializedPaymentOffer)", html);
        Assert.DoesNotContain("sessionStorage.setItem('chronoBortzDifference', chronoBortzDifference.toFixed(2));", html);
        Assert.DoesNotContain("sessionStorage.setItem('chronoPhenoDifference', chronoPhenoDifference.toFixed(2));", html);
        Assert.DoesNotContain("sessionStorage.setItem('biomarkerData', JSON.stringify(biomarkerData));", html);
        Assert.DoesNotContain("sessionStorage.setItem(PENDING_PAYMENT_OFFER_KEY, serializedPaymentOffer);", html);
        Assert.DoesNotContain("sessionStorage.setItem(PENDING_PAYMENT_OFFER_KEY, JSON.stringify(", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.')\n                    .then(() => document.getElementById('continueButton')?.focus());", html);
        Assert.Contains("if (!storeBortzResultForNextStep(biomarkerData, chronoBortzDifference, chronoPhenoDifference)) return;", html);

        var failureBody = GetStoreFailureBody(html, "function storeBortzResultForNextStep");
        Assert.Contains("clearStoredBiomarkerHandoff();", failureBody);
        Assert.Contains("if (!isUpdate) clearPendingPaymentOffer();", failureBody);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.')\n                    .then(() => document.getElementById('continueButton')?.focus());", failureBody);
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

    private static string GetBioageFlowPath()
    {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "js", "bioage-flow.js");
    }

    private static string GetStoreFailureBody(string html, string storeFunctionMarker)
    {
        var storeStart = html.IndexOf(storeFunctionMarker, StringComparison.Ordinal);
        var failureStart = html.IndexOf("if (!stored) {", storeStart, StringComparison.Ordinal);
        var returnFalse = html.IndexOf("return false;", failureStart, StringComparison.Ordinal);

        Assert.True(storeStart >= 0);
        Assert.True(failureStart > storeStart);
        Assert.True(returnFalse > failureStart);

        return html[failureStart..returnFalse];
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
