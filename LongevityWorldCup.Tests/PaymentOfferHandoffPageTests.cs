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
        Assert.Contains("function serializePendingPaymentOffer(offer)", html);
        Assert.Contains("if (!isUsablePaymentOffer(offer)) return null;", html);
        Assert.Contains("function isUsablePaymentOffer(paymentOffer)", html);
        Assert.Contains("typeof paymentOffer.source === \"string\"", html);
        Assert.Contains("typeof paymentOffer.offerType === \"string\"", html);
        Assert.Contains("typeof paymentOffer.currency === \"string\"", html);
        Assert.Contains("typeof paymentOffer.amountUsd === \"number\"", html);
        Assert.Contains("Number.isFinite(paymentOffer.amountUsd)", html);
        Assert.Contains("paymentOffer.amountUsd >= 0", html);
        Assert.Contains("const serializedOffer = JSON.stringify(offer);", html);
        Assert.Contains("catch (_) {", html);
        Assert.Contains("const serializedOffer = serializePendingPaymentOffer(effectiveOffer);", html);
        Assert.Contains("customAlert('Payment details could not be saved. Enable browser storage and try again.');", html);
        Assert.Contains("return false;", html);
        Assert.Contains("function setSessionItem(key, value)", html);
        Assert.Contains("if (serializedOffer && setSessionItem(PENDING_PAYMENT_OFFER_KEY, serializedOffer))", html);
        Assert.Contains("const stored = setPendingPaymentOffer({", html);
        Assert.Contains("if (!stored) return;", html);
        Assert.Contains("if (!setPendingPaymentOffer(paymentOffer)) return;", html);
        Assert.DoesNotContain("setSessionItem(PENDING_PAYMENT_OFFER_KEY, JSON.stringify(effectiveOffer))", html);
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
        Assert.Contains("catch (_) {", html);
        Assert.Contains("const serializedOffer = serializePendingPaymentOffer(effectiveOffer);", html);
        Assert.Contains("customAlert('Payment details could not be saved. Enable browser storage and try again.');", html);
        Assert.Contains("return false;", html);
        Assert.Contains("function setSessionItem(key, value)", html);
        Assert.Contains("if (serializedOffer && setSessionItem(PENDING_PAYMENT_OFFER_KEY, serializedOffer))", html);
        Assert.Contains("if (typeof beforeNavigate === 'function' && beforeNavigate() === false) return;", html);
        Assert.Contains("return setPendingPaymentOffer(paymentOffer);", html);
        Assert.DoesNotContain("setSessionItem(PENDING_PAYMENT_OFFER_KEY, JSON.stringify(effectiveOffer))", html);
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
