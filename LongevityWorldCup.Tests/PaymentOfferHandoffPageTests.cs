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
        var offerStart = html.IndexOf("function setPendingPaymentOffer(offer, retryButton)", StringComparison.Ordinal);
        var serializeStart = html.IndexOf("const serializedOffer = serializePendingPaymentOffer(effectiveOffer);", offerStart, StringComparison.Ordinal);

        Assert.True(offerStart >= 0);
        Assert.True(serializeStart > offerStart);

        var offerAdjustmentBody = html[offerStart..serializeStart];

        Assert.Contains("function setPendingPaymentOffer(offer, retryButton)", html);
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
        Assert.Contains("let effectiveOffer = offer;", offerAdjustmentBody);
        Assert.Contains("try {", offerAdjustmentBody);
        Assert.Contains("window.applyPaymentAdjustmentsToPaymentOffer(offer)", offerAdjustmentBody);
        Assert.Contains("window.applyFreePassToPaymentOffer(offer)", offerAdjustmentBody);
        Assert.Contains("catch (_) {", offerAdjustmentBody);
        Assert.Contains("customAlert('Payment details could not be saved. Enable browser storage and try again.')\n            .then(() => retryButton?.focus());", offerAdjustmentBody);
        Assert.Contains("return false;", offerAdjustmentBody);
        Assert.Contains("const serializedOffer = serializePendingPaymentOffer(effectiveOffer);", html);
        Assert.Contains("customAlert('Payment details could not be saved. Enable browser storage and try again.')\n          .then(() => retryButton?.focus());", html);
        Assert.Contains("return false;", html);
        Assert.Contains("function setSessionItem(key, value)", html);
        Assert.Contains("if (serializedOffer && setSessionItem(PENDING_PAYMENT_OFFER_KEY, serializedOffer))", html);
        Assert.Contains("onclick=\"startAmateurApplication(this)\"", html);
        Assert.Contains("onclick=\"startProApplication(this)\"", html);
        Assert.Contains("function startAmateurApplication(retryButton)", html);
        Assert.Contains("const stored = setPendingPaymentOffer({", html);
        Assert.Contains("}, retryButton);", html);
        Assert.Contains("if (!stored) return;", html);
        Assert.Contains("function startProApplication(retryButton)", html);
        Assert.Contains("if (!setPendingPaymentOffer(paymentOffer, retryButton)) return;", html);
        Assert.Contains("function preserveAppliedDiscountMetadata(offer, result)", html);
        Assert.Contains("if (!hasDiscountCode || !window.addActiveDiscountMetadataToPaymentOffer) return offer;", html);
        Assert.Contains("return window.addActiveDiscountMetadataToPaymentOffer(offer);", html);
        Assert.Contains("return null;", html);
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
        var offerStart = html.IndexOf("function setPendingPaymentOffer(offer, retryButton)", StringComparison.Ordinal);
        var serializeStart = html.IndexOf("const serializedOffer = serializePendingPaymentOffer(effectiveOffer);", offerStart, StringComparison.Ordinal);

        Assert.True(offerStart >= 0);
        Assert.True(serializeStart > offerStart);

        var offerAdjustmentBody = html[offerStart..serializeStart];

        Assert.Contains("function setPendingPaymentOffer(offer, retryButton)", html);
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
        Assert.Contains("let effectiveOffer = offer;", offerAdjustmentBody);
        Assert.Contains("try {", offerAdjustmentBody);
        Assert.Contains("window.applyPaymentAdjustmentsToPaymentOffer(offer)", offerAdjustmentBody);
        Assert.Contains("window.applyFreePassToPaymentOffer(offer)", offerAdjustmentBody);
        Assert.Contains("catch (_) {", offerAdjustmentBody);
        Assert.Contains("customAlert('Payment details could not be saved. Enable browser storage and try again.')\n                    .then(() => retryButton?.focus());", offerAdjustmentBody);
        Assert.Contains("return false;", offerAdjustmentBody);
        Assert.Contains("const serializedOffer = serializePendingPaymentOffer(effectiveOffer);", html);
        Assert.Contains("customAlert('Payment details could not be saved. Enable browser storage and try again.')\n                .then(() => retryButton?.focus());", html);
        Assert.Contains("return false;", html);
        Assert.Contains("function setSessionItem(key, value)", html);
        Assert.Contains("if (serializedOffer && setSessionItem(PENDING_PAYMENT_OFFER_KEY, serializedOffer))", html);
        Assert.Contains("if (typeof beforeNavigate === 'function' && beforeNavigate(button) === false) return;", html);
        Assert.Contains("function preserveAppliedDiscountMetadata(offer, result)", html);
        Assert.Contains("if (!hasDiscountCode || !window.addActiveDiscountMetadataToPaymentOffer) return offer;", html);
        Assert.Contains("return window.addActiveDiscountMetadataToPaymentOffer(offer);", html);
        Assert.Contains("return null;", html);
        Assert.Contains("const paymentOffer = preserveAppliedDiscountMetadata({", html);
        Assert.Contains("return setPendingPaymentOffer(paymentOffer, button);", html);
        Assert.DoesNotContain("paymentOffer = window.addActiveDiscountMetadataToPaymentOffer(paymentOffer);", html);
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
