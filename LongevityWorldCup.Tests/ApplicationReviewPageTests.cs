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
        Assert.Contains("function normalizePaymentSubmissionType(value)", script);
        Assert.Contains("return value === \"application\" || value === \"result\" || value === \"edit\" ? value : null;", script);
        Assert.Contains("function normalizeOptionalString(value)", script);
        Assert.Contains("return typeof value === \"string\" && value.trim() ? value.trim() : null;", script);
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
        var pendingParseStart = script.IndexOf("function parsePendingPaymentInvoice(value, clearMalformedInvoice)", StringComparison.Ordinal);
        var pendingParseEnd = script.IndexOf("function readPendingPaymentInvoice()", pendingParseStart, StringComparison.Ordinal);
        Assert.True(pendingParseStart >= 0);
        Assert.True(pendingParseEnd > pendingParseStart);
        var pendingParseBody = script[pendingParseStart..pendingParseEnd];
        Assert.Contains("if (!value) return null;", pendingParseBody);
        Assert.Contains("const parsed = JSON.parse(value);", pendingParseBody);
        Assert.Contains("if (parsed && typeof parsed === 'object' && !Array.isArray(parsed))", pendingParseBody);
        Assert.Contains("const invoiceId = typeof parsed.invoiceId === 'string' ? parsed.invoiceId.trim() : '';", pendingParseBody);
        Assert.Contains("if (invoiceId)", pendingParseBody);
        Assert.Contains("return { ...parsed, invoiceId };", pendingParseBody);
        Assert.Contains("clearMalformedInvoice();", pendingParseBody);
        Assert.Contains("return null;", pendingParseBody);
        var pendingReadStart = script.IndexOf("function readPendingPaymentInvoice()", StringComparison.Ordinal);
        var pendingReadEnd = script.IndexOf("function readStoredContactEmail()", pendingReadStart, StringComparison.Ordinal);
        Assert.True(pendingReadStart >= 0);
        Assert.True(pendingReadEnd > pendingReadStart);
        var pendingReadBody = script[pendingReadStart..pendingReadEnd];
        Assert.Contains("parsePendingPaymentInvoice(", pendingReadBody);
        Assert.Contains("getSessionItem(PENDING_PAYMENT_INVOICE_KEY)", pendingReadBody);
        Assert.Contains("() => removeSessionItem(PENDING_PAYMENT_INVOICE_KEY)", pendingReadBody);
        Assert.Contains("getLocalItem(PENDING_PAYMENT_INVOICE_STORAGE_KEY)", pendingReadBody);
        Assert.Contains("() => removeLocalItem(PENDING_PAYMENT_INVOICE_STORAGE_KEY)", pendingReadBody);
        Assert.DoesNotContain("getSessionItem(PENDING_PAYMENT_INVOICE_KEY)\r\n                    || getLocalItem(PENDING_PAYMENT_INVOICE_STORAGE_KEY)", pendingReadBody);
        Assert.DoesNotContain("getSessionItem(PENDING_PAYMENT_INVOICE_KEY)\n                    || getLocalItem(PENDING_PAYMENT_INVOICE_STORAGE_KEY)", pendingReadBody);
        Assert.DoesNotContain("} catch (_) {", pendingReadBody);
        Assert.Contains("const contactEmail = readStoredContactEmail();", script);
        Assert.Contains("accountEmail: normalizeContactEmail(pending.accountEmail) || contactEmail || null", script);
        Assert.Contains("applicantName: normalizeOptionalString(pending.applicantName)", script);
        Assert.Contains("submissionType: normalizePaymentSubmissionType(pending.submissionType)", script);
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
        Assert.Contains("if (typeof value !== 'string') return null;", script);
        Assert.Contains("let contactEmail = (value || '').trim();", script);
        Assert.Contains("const bracketedEmail = /<([^<>]+)>/.exec(contactEmail);", script);
        Assert.Contains("contactEmail.replace(/^mailto:/i, '').split('?')[0].trim();", script);
        Assert.Contains("emailInput.type = 'email';", script);
        Assert.Contains("return emailInput.checkValidity() ? contactEmail : null;", script);
        Assert.Contains("function readStoredContactEmail()", script);
        var contactReadStart = script.IndexOf("function readStoredContactEmail()", StringComparison.Ordinal);
        var contactReadEnd = script.IndexOf("const PENDING_PAYMENT_INVOICE_KEY", contactReadStart, StringComparison.Ordinal);
        Assert.True(contactReadStart >= 0);
        Assert.True(contactReadEnd > contactReadStart);
        var contactReadBody = script[contactReadStart..contactReadEnd];
        Assert.Contains("const sessionContactEmail = normalizeContactEmail(getSessionItem('contactEmail'));", script);
        Assert.Contains("if (sessionContactEmail) return sessionContactEmail;", script);
        Assert.Contains("const localContactEmail = normalizeContactEmail(getLocalItem('contactEmail'));", script);
        Assert.Contains("if (localContactEmail)", script);
        Assert.Contains("return localContactEmail;", script);
        Assert.Contains("normalizeContactEmail(pendingPaymentInvoice && pendingPaymentInvoice.accountEmail)", script);
        Assert.Contains("if (pendingContactEmail)", script);
        Assert.Contains("return pendingContactEmail;", script);
        var pendingContactIndex = contactReadBody.IndexOf("const pendingContactEmail = normalizeContactEmail(pendingPaymentInvoice && pendingPaymentInvoice.accountEmail)", StringComparison.Ordinal);
        var sessionContactIndex = contactReadBody.IndexOf("const sessionContactEmail = normalizeContactEmail(getSessionItem('contactEmail'));", StringComparison.Ordinal);
        Assert.True(pendingContactIndex >= 0);
        Assert.True(sessionContactIndex >= 0);
        Assert.True(pendingContactIndex < sessionContactIndex);
        Assert.Contains("removeSessionItem('contactEmail');", script);
        Assert.Contains("removeLocalItem('contactEmail');", script);
        Assert.Contains("? 'the email address you provided'", script);
        Assert.Contains(": 'the email address you provided with your application';", script);
        Assert.Contains("accountEmail: normalizeContactEmail(pending.accountEmail) || contactEmail || null", script);
        Assert.Contains("applicantName: normalizeOptionalString(pending.applicantName)", script);
        Assert.Contains("submissionType: normalizePaymentSubmissionType(pending.submissionType)", script);
    }

    [Fact]
    public async Task ApplicationReview_WrapsLongStoredContactEmail()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/application-review.html");
        var ruleStart = html.IndexOf("#contactEmailPlaceholder", StringComparison.Ordinal);
        Assert.True(ruleStart >= 0, "Could not find contact email wrapping rule.");

        var ruleEnd = html.IndexOf('}', ruleStart);
        Assert.True(ruleEnd > ruleStart, "Could not find end of contact email wrapping rule.");

        var rule = html[ruleStart..ruleEnd];
        Assert.Contains("overflow-wrap: anywhere;", rule);
        Assert.Contains("word-break: break-word;", rule);
        Assert.Contains("white-space: normal;", rule);
    }
}
