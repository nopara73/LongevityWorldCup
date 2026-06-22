using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PaymentOfferHandoffPageTests
{
    [Fact]
    public async Task JoinGamePaymentOffer_HaltsNavigationWhenStorageFails()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/join-game.html");

        Assert.Contains("function setPendingPaymentOffer(offer)", html);
        Assert.Contains("return true;", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", html);
        Assert.Contains("return false;", html);
        Assert.Contains("function setSessionItem(key, value)", html);
        Assert.Contains("if (setSessionItem(PENDING_PAYMENT_OFFER_KEY, JSON.stringify(effectiveOffer)))", html);
        Assert.Contains("const stored = setPendingPaymentOffer({", html);
        Assert.Contains("if (!stored) return;", html);
        Assert.Contains("if (!setPendingPaymentOffer(paymentOffer)) return;", html);
        Assert.DoesNotContain("sessionStorage.setItem(PENDING_PAYMENT_OFFER_KEY", html);
    }

    [Fact]
    public async Task JoinGamePricing_RendersWhenModuleReadinessRejects()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/join-game.html");

        Assert.Contains("const ready = Promise.resolve(window.modulesReady || undefined).catch(() => {});", html);
        Assert.Contains(".then(() => ensureProDiscountsLoaded())", html);
        Assert.Contains(".catch(() => {})", html);
        Assert.Contains(".then(() => renderProDiscountNote());", html);
        Assert.DoesNotContain("const ready = window.modulesReady || Promise.resolve();", html);
    }

    [Fact]
    public async Task CharacterCustomizationPaymentOffer_HaltsNavigationWhenStorageFails()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-customization.html");

        Assert.Contains("function setPendingPaymentOffer(offer)", html);
        Assert.Contains("return true;", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", html);
        Assert.Contains("return false;", html);
        Assert.Contains("function setSessionItem(key, value)", html);
        Assert.Contains("if (setSessionItem(PENDING_PAYMENT_OFFER_KEY, JSON.stringify(effectiveOffer)))", html);
        Assert.Contains("if (typeof beforeNavigate === 'function' && beforeNavigate() === false) return;", html);
        Assert.Contains("return setPendingPaymentOffer(paymentOffer);", html);
        Assert.DoesNotContain("sessionStorage.setItem(PENDING_PAYMENT_OFFER_KEY", html);
    }

    [Fact]
    public async Task CharacterCustomizationPaymentOfferClear_DoesNotBlockResultNavigation()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-customization.html");
        var clearStart = html.IndexOf("function clearPendingPaymentOffer()", StringComparison.Ordinal);
        var clearEnd = html.IndexOf("document.addEventListener('DOMContentLoaded'", clearStart, StringComparison.Ordinal);

        Assert.True(clearStart >= 0);
        Assert.True(clearEnd > clearStart);

        var clearBody = html[clearStart..clearEnd];

        Assert.Contains("removeSessionItem(PENDING_PAYMENT_OFFER_KEY);", clearBody);
        Assert.DoesNotContain("sessionStorage.removeItem(PENDING_PAYMENT_OFFER_KEY);", clearBody);
        Assert.Contains("() => clearPendingPaymentOffer()", html);
    }
}
