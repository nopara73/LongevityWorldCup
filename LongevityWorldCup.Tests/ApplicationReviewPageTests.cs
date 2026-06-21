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
        Assert.Contains("const isNewResultsUploaded = getSessionItem(\"came-from\") === \"proof-upload\";", script);
        Assert.Contains("const isEditRequest = getSessionItem(\"came-from\") === \"edit-profile\";", script);
        Assert.Contains("setLocalItem('hasApplication', 'true');", script);
        Assert.Contains("accountEmail: pending.accountEmail || contactEmail || null", script);
        Assert.Contains("removeSessionItem(PENDING_PAYMENT_INVOICE_KEY);", script);
        Assert.Contains("removeLocalItem(PENDING_PAYMENT_INVOICE_STORAGE_KEY);", script);
        Assert.DoesNotContain("sessionStorage.getItem(", script);
        Assert.DoesNotContain("sessionStorage.setItem(", script);
        Assert.DoesNotContain("sessionStorage.removeItem(", script);
        Assert.DoesNotContain("localStorage.getItem(", script);
        Assert.DoesNotContain("localStorage.setItem(", script);
        Assert.DoesNotContain("localStorage.removeItem(", script);
    }
}
