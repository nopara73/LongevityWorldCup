using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PaymentOfferHandoffPageTests
{
    [Fact]
    public async Task JoinPaymentOffer_HaltsNavigationWhenStorageFails()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");
        var playMenu = await client.GetStringAsync("/js/play-menu.js");
        var offerStart = flow.IndexOf("function setPendingPaymentOffer(offer, retryButton)", StringComparison.Ordinal);
        var serializeStart = flow.IndexOf("const serializedOffer = serializePendingPaymentOffer(effectiveOffer);", offerStart, StringComparison.Ordinal);

        Assert.True(offerStart >= 0);
        Assert.True(serializeStart > offerStart);

        var offerAdjustmentBody = flow[offerStart..serializeStart];

        Assert.Contains("function setPendingPaymentOffer(offer, retryButton)", flow);
        Assert.Contains("return true;", flow);
        Assert.Contains("function serializePendingPaymentOffer(offer)", flow);
        Assert.Contains("if (!isUsablePaymentOffer(offer)) return null;", flow);
        Assert.Contains("function isUsablePaymentOffer(paymentOffer)", flow);
        Assert.Contains("typeof paymentOffer.source === \"string\"", flow);
        Assert.Contains("typeof paymentOffer.offerType === \"string\"", flow);
        Assert.Contains("typeof paymentOffer.currency === \"string\"", flow);
        Assert.Contains("typeof paymentOffer.amountUsd === \"number\"", flow);
        Assert.Contains("Number.isFinite(paymentOffer.amountUsd)", flow);
        Assert.Contains("paymentOffer.amountUsd >= 0", flow);
        Assert.Contains("const serializedOffer = JSON.stringify(offer);", flow);
        Assert.Contains("let effectiveOffer = offer;", offerAdjustmentBody);
        Assert.Contains("try {", offerAdjustmentBody);
        Assert.Contains("window.applyPaymentAdjustmentsToPaymentOffer", offerAdjustmentBody);
        Assert.Contains("window.applyFreePassToPaymentOffer", offerAdjustmentBody);
        Assert.Contains("catch (_) {", offerAdjustmentBody);
        Assert.Contains("notifyPaymentStorageFailure(retryButton);", offerAdjustmentBody);
        Assert.Contains("return false;", offerAdjustmentBody);
        Assert.Contains("const serializedOffer = serializePendingPaymentOffer(effectiveOffer);", flow);
        Assert.Contains("function notifyPaymentStorageFailure(retryButton)", flow);
        Assert.Contains("Payment details could not be saved. Enable browser storage and try again.", flow);
        Assert.Contains("function setSessionItem(key, value)", flow);
        Assert.Contains("if (serializedOffer && setSessionItem(PENDING_PAYMENT_OFFER_KEY, serializedOffer))", flow);
        Assert.Contains("function startAmateurApplication(retryButton)", playMenu);
        Assert.Contains("const stored = flow.setPendingPaymentOffer({", playMenu);
        Assert.Contains("}, retryButton);", playMenu);
        Assert.Contains("if (!stored) return;", playMenu);
        Assert.Contains("function startProApplication(retryButton)", playMenu);
        Assert.Contains("if (!flow.setPendingPaymentOffer(paymentOffer, retryButton)) return;", playMenu);
        Assert.Contains("function preserveAppliedDiscountMetadata(offer, result)", flow);
        Assert.Contains("if (!hasDiscountCode || !window.addActiveDiscountMetadataToPaymentOffer) return offer;", flow);
        Assert.Contains("return window.addActiveDiscountMetadataToPaymentOffer(offer);", flow);
        Assert.Contains("return null;", flow);
        Assert.DoesNotContain("setSessionItem(PENDING_PAYMENT_OFFER_KEY, JSON.stringify(effectiveOffer))", flow);
        Assert.DoesNotContain("sessionStorage.setItem(PENDING_PAYMENT_OFFER_KEY", flow);
    }

    [Fact]
    public async Task JoinPricing_RendersWhenModuleReadinessRejects()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var playMenu = await client.GetStringAsync("/js/play-menu.js");

        Assert.Contains("return Promise.resolve(window.modulesReady || undefined)", playMenu);
        Assert.Contains(".catch(() => {})", playMenu);
        Assert.Contains("renderJoinPricing();", playMenu);
        Assert.Contains("if (!window.proDiscounts || typeof window.proDiscounts.buildDiscountBreakdown !== 'function') return;", playMenu);
        Assert.DoesNotContain("const ready = window.modulesReady || Promise.resolve();", playMenu);
    }

    [Fact]
    public async Task JoinPricing_DiscountBadgeSlotFitsMobileTapTarget()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var css = await client.GetStringAsync("/css/play-menu.css");

        Assert.Contains(".pro-discount-badge-slot {\n    width: 44px;", css);
        Assert.Contains("min-width: 44px;", css);
        Assert.Contains("height: 44px;", css);
        Assert.Contains(".pro-discount-badge-slot:empty", css);
        Assert.Contains(".pro-discount-breakdown.pro-discount-breakdown--with-badges .pro-discount-badge-slot:empty", css);
        Assert.Contains(".pro-discount-text", css);
        Assert.Contains("overflow-wrap: anywhere;", css);
    }

    [Fact]
    public async Task SharedDashboardPaymentOffer_HaltsNavigationWhenStorageFails()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");
        var playMenu = await client.GetStringAsync("/js/play-menu.js");
        var offerStart = flow.IndexOf("function setPendingPaymentOffer(offer, retryButton)", StringComparison.Ordinal);
        var serializeStart = flow.IndexOf("const serializedOffer = serializePendingPaymentOffer(effectiveOffer);", offerStart, StringComparison.Ordinal);

        Assert.True(offerStart >= 0);
        Assert.True(serializeStart > offerStart);

        var offerAdjustmentBody = flow[offerStart..serializeStart];

        Assert.Contains("flow.renderDashboardActions(athlete, {", playMenu);
        Assert.Contains("function setPendingPaymentOffer(offer, retryButton)", flow);
        Assert.Contains("return true;", flow);
        Assert.Contains("function serializePendingPaymentOffer(offer)", flow);
        Assert.Contains("if (!isUsablePaymentOffer(offer)) return null;", flow);
        Assert.Contains("function isUsablePaymentOffer(paymentOffer)", flow);
        Assert.Contains("typeof paymentOffer.source === \"string\"", flow);
        Assert.Contains("typeof paymentOffer.offerType === \"string\"", flow);
        Assert.Contains("typeof paymentOffer.currency === \"string\"", flow);
        Assert.Contains("typeof paymentOffer.amountUsd === \"number\"", flow);
        Assert.Contains("Number.isFinite(paymentOffer.amountUsd)", flow);
        Assert.Contains("paymentOffer.amountUsd >= 0", flow);
        Assert.Contains("const serializedOffer = JSON.stringify(offer);", flow);
        Assert.Contains("let effectiveOffer = offer;", offerAdjustmentBody);
        Assert.Contains("try {", offerAdjustmentBody);
        Assert.Contains("window.applyPaymentAdjustmentsToPaymentOffer", offerAdjustmentBody);
        Assert.Contains("window.applyFreePassToPaymentOffer", offerAdjustmentBody);
        Assert.Contains("catch (_) {", offerAdjustmentBody);
        Assert.Contains("notifyPaymentStorageFailure(retryButton);", offerAdjustmentBody);
        Assert.Contains("return false;", offerAdjustmentBody);
        Assert.Contains("const serializedOffer = serializePendingPaymentOffer(effectiveOffer);", flow);
        Assert.Contains("function notifyPaymentStorageFailure(retryButton)", flow);
        Assert.Contains("Payment details could not be saved. Enable browser storage and try again.", flow);
        Assert.Contains("if (serializedOffer && setSessionItem(PENDING_PAYMENT_OFFER_KEY, serializedOffer))", flow);
        Assert.Contains("if (typeof beforeNavigate === \"function\" && beforeNavigate(button) === false) return;", flow);
        Assert.Contains("function preserveAppliedDiscountMetadata(offer, result)", flow);
        Assert.Contains("if (!hasDiscountCode || !window.addActiveDiscountMetadataToPaymentOffer) return offer;", flow);
        Assert.Contains("return window.addActiveDiscountMetadataToPaymentOffer(offer);", flow);
        Assert.Contains("return null;", flow);
        Assert.Contains("const paymentOffer = preserveAppliedDiscountMetadata({", flow);
        Assert.Contains("return setPendingPaymentOffer(paymentOffer, button);", flow);
        Assert.DoesNotContain("paymentOffer = window.addActiveDiscountMetadataToPaymentOffer(paymentOffer);", flow);
        Assert.DoesNotContain("setSessionItem(PENDING_PAYMENT_OFFER_KEY, JSON.stringify(effectiveOffer))", flow);
        Assert.DoesNotContain("sessionStorage.setItem(PENDING_PAYMENT_OFFER_KEY", flow);
    }

    [Fact]
    public async Task SharedDashboardPaymentOfferClear_DoesNotBlockResultNavigation()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");
        var clearStart = flow.IndexOf("function clearPendingPaymentOffer()", StringComparison.Ordinal);
        var clearEnd = flow.IndexOf("function createPriceHtmlFallback", clearStart, StringComparison.Ordinal);

        Assert.True(clearStart >= 0);
        Assert.True(clearEnd > clearStart);

        var clearBody = flow[clearStart..clearEnd];

        Assert.Contains("removeSessionItem(PENDING_PAYMENT_OFFER_KEY);", clearBody);
        Assert.DoesNotContain("sessionStorage.removeItem(PENDING_PAYMENT_OFFER_KEY);", clearBody);
        Assert.Contains("() => clearPendingPaymentOffer()", flow);
    }
}
