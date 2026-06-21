using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ApplicationReviewPageTests
{
    [Fact]
    public async Task ApplicationReview_UsesSafeStorageAccessForSubmissionContext()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/application-review.html");
        var scriptStart = html.IndexOf("function getStorageItem(storageName, key)", StringComparison.Ordinal);
        var scriptEnd = html.IndexOf("</script>", scriptStart, StringComparison.Ordinal);

        Assert.True(scriptStart >= 0);
        Assert.True(scriptEnd > scriptStart);

        var script = html[scriptStart..scriptEnd];

        Assert.Contains("function getStorageItem(storageName, key)", script);
        Assert.Contains("return window[storageName].getItem(key);", script);
        Assert.Contains("function setStorageItem(storageName, key, value)", script);
        Assert.Contains("window[storageName].setItem(key, value);", script);
        Assert.Contains("function removeStorageItem(storageName, key)", script);
        Assert.Contains("window[storageName].removeItem(key);", script);
        Assert.Contains("function normalizeReviewSource(value)", script);
        Assert.Contains("return value === \"proof-upload\" || value === \"edit-profile\" ? value : null;", script);
        Assert.Contains("function normalizeSubmissionTypeReviewSource(value)", script);
        Assert.Contains("if (value === \"result\") return \"proof-upload\";", script);
        Assert.Contains("if (value === \"edit\") return \"edit-profile\";", script);
        Assert.Contains("function readReviewSource()", script);
        Assert.Contains("normalizeReviewSource(new URLSearchParams(window.location.search).get(\"from\"))", script);
        Assert.Contains("|| normalizeReviewSource(getSessionItem(\"came-from\"))", script);
        Assert.Contains("|| normalizeReviewSource(pendingPaymentInvoice && pendingPaymentInvoice.reviewSource)", script);
        Assert.Contains("|| normalizeSubmissionTypeReviewSource(pendingPaymentInvoice && pendingPaymentInvoice.submissionType);", script);
        Assert.Contains("const reviewSource = readReviewSource();", script);
        Assert.Contains("const isNewResultsUploaded = reviewSource === \"proof-upload\";", script);
        Assert.Contains("const isEditRequest = reviewSource === \"edit-profile\";", script);
        Assert.Contains("document.getElementById(\"appReviewText\").textContent = \"Result review\";", script);
        Assert.Contains("document.getElementById(\"whatAreWeReviewing\").textContent = \"your new results\";", script);
        Assert.Contains("setLocalItem('hasApplication', 'true');", script);
        Assert.Contains("const pendingPaymentInvoice = readPendingPaymentInvoice();", script);
        var pendingReadStart = script.IndexOf("function readPendingPaymentInvoice()", StringComparison.Ordinal);
        var pendingReadEnd = script.IndexOf("function readStoredContactEmail()", pendingReadStart, StringComparison.Ordinal);
        Assert.True(pendingReadStart >= 0);
        Assert.True(pendingReadEnd > pendingReadStart);
        var pendingReadBody = script[pendingReadStart..pendingReadEnd];
        Assert.Contains("} catch (_) {", pendingReadBody);
        Assert.Contains("removeSessionItem(PENDING_PAYMENT_INVOICE_KEY);", pendingReadBody);
        Assert.Contains("removeLocalItem(PENDING_PAYMENT_INVOICE_STORAGE_KEY);", pendingReadBody);
        Assert.Contains("return null;", pendingReadBody);
        Assert.Contains("const contactEmail = readStoredContactEmail();", script);
        Assert.Contains("accountEmail: normalizeContactEmail(pending.accountEmail) || contactEmail || null", script);
        Assert.Contains("submissionType: pending.submissionType || null", script);
        Assert.Contains("removeSessionItem(PENDING_PAYMENT_INVOICE_KEY);", script);
        Assert.Contains("removeLocalItem(PENDING_PAYMENT_INVOICE_STORAGE_KEY);", script);
        Assert.DoesNotContain("sessionStorage.getItem(", script);
        Assert.DoesNotContain("sessionStorage.setItem(", script);
        Assert.DoesNotContain("sessionStorage.removeItem(", script);
        Assert.DoesNotContain("localStorage.getItem(", script);
        Assert.DoesNotContain("localStorage.setItem(", script);
        Assert.DoesNotContain("localStorage.removeItem(", script);
    }

    [Fact]
    public async Task ApplicationReview_NormalizesStoredContactEmailBeforeDisplayAndPaymentCheck()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/application-review.html");
        var scriptStart = html.IndexOf("function normalizeContactEmail(value)", StringComparison.Ordinal);
        var scriptEnd = html.IndexOf("</script>", scriptStart, StringComparison.Ordinal);

        Assert.True(scriptStart >= 0);
        Assert.True(scriptEnd > scriptStart);

        var script = html[scriptStart..scriptEnd];

        Assert.Contains("function normalizeContactEmail(value)", script);
        Assert.Contains("const contactEmail = (value || '').trim();", script);
        Assert.Contains("emailInput.type = 'email';", script);
        Assert.Contains("return emailInput.checkValidity() ? contactEmail : null;", script);
        Assert.Contains("function readStoredContactEmail()", script);
        Assert.Contains("normalizeContactEmail(getSessionItem('contactEmail') || getLocalItem('contactEmail'))", script);
        Assert.Contains("normalizeContactEmail(pendingPaymentInvoice && pendingPaymentInvoice.accountEmail)", script);
        Assert.Contains("removeSessionItem('contactEmail');", script);
        Assert.Contains("removeLocalItem('contactEmail');", script);
        Assert.Contains("? 'the email address you provided'", script);
        Assert.Contains(": 'the email address you provided with your application';", script);
        Assert.Contains("accountEmail: normalizeContactEmail(pending.accountEmail) || contactEmail || null", script);
    }
}
