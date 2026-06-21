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
        Assert.Contains("return JSON.parse(getSessionItem('biomarkerData'));", html);
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
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void BioagePages_ReplaceMalformedPendingPaymentOfferBeforeHandoff(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));

        Assert.Contains("function hasUsablePendingPaymentOffer()", html);
        Assert.Contains("const rawOffer = getSessionItem(PENDING_PAYMENT_OFFER_KEY);", html);
        Assert.Contains("const parsedOffer = JSON.parse(rawOffer);", html);
        Assert.Contains("if (parsedOffer && typeof parsedOffer === 'object' && !Array.isArray(parsedOffer)) return true;", html);
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
    }

    private static string GetPagePath(string fileName)
    {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "onboarding", fileName);
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
